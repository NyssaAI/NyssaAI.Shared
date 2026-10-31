# Responsibility: Coordinate two-phase prepare, execute, reconcile, and recover operations across local documents and cloud vaults.
# Public interface:
# `-- ConfigurationRecoveryCoordinator
#     +-- attempt(result, state)
#     +-- cleanup_steps(steps)
#     +-- new(document_factory, get_vault, get_cipher, protector, logger)
#     +-- outcome(state)
#     +-- prepare_step(provider_name, action, namespace, key, value, source, is_json)
#     +-- recover(handle)
#     `-- write_steps(steps, proposed)
"""Two-phase recovery coordinator matching .NET ConfigurationRecoveryCoordinator."""

from collections.abc import Callable, Mapping
from dataclasses import replace
import json
import uuid

from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.cloud_key_vaults.types import (
    CloudKeyVaultAction,
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    ConfigurationPointer,
)
from nyssa_ai_configuration.encryption.configuration_cipher import ConfigurationCipher
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.recovery.configuration_recovery_handle import ConfigurationRecoveryHandle
from nyssa_ai_configuration.recovery.configuration_recovery_token_protector import ConfigurationRecoveryTokenProtector
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationOperationProgress,
    ConfigurationRecoveryOutcome,
    ConfigurationRecoveryState,
    ConfigurationRecoveryStatus,
    ConfigurationRecoveryStep,
    ConfigurationStepProgress,
)
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_document_local_file import ConfigurationDocumentLocalFile
from nyssa_ai_configuration.values.types import ConfigurationValue

_CONFLICT_MSG = "Configuration changed after this operation. Recovery stopped."
_MANUAL_MSG = "The provider cannot establish safe recovery. Reconcile the recorded operation manually."
_UNFINISHED_MSG = "The configuration operation is unfinished."


class ConfigurationRecoveryCoordinator:
    """Executes guarded two-phase multi-step operations and resumes interrupted handles."""

    def __init__(
        self,
        document_factory: Callable[[str], ConfigurationDocumentLocalFile],
        get_vault: Callable[[str], Result[CloudKeyVault]],
        get_cipher: Callable[[], Result[ConfigurationCipher]],
        protector: ConfigurationRecoveryTokenProtector | None = None,
        logger: ConfigurationLogger | None = None,
    ) -> None:
        """Initialize with document factory, vault lookup, cipher lookup, token protector, and logger."""
        self._documents = document_factory
        self._get_vault = get_vault
        self._get_cipher = get_cipher
        self._protector = protector
        self._logger = logger

    def prepare_step(
        self, provider_name: str, action: CloudKeyVaultAction, namespace: str, key: str,
        value: str | None, source: ConfigurationValue | None, is_json: bool,
    ) -> Result[ConfigurationRecoveryStep]:
        """Prepare a local or remote step before executing multi-key or move mutations."""
        is_local = provider_name in ("", "aesLocal", "oauth")
        vault: CloudKeyVault | None = None
        if not is_local:
            found = self._get_vault(provider_name)
            if not found.ok:
                return found
            vault = found.value
        pointer = ConfigurationPointer(provider=provider_name, value=source.value) if vault is not None and source is not None else None
        op = CloudKeyVaultOperation(operation_id=str(uuid.uuid4()), action=action, namespace=namespace, key=key, value=value, pointer=pointer)
        prep: CloudKeyVaultPreparation | None = None
        if vault is not None and vault.recovery is not None:
            prep_res = vault.recovery.prepare(op)
            if not prep_res.ok:
                return prep_res
            if not prep_res.value.store_identity.strip() or prep_res.value.store_identity != vault.recovery.store_identity:
                return fail(_CONFLICT_MSG, "Conflict")
            prep = prep_res.value
        target_ref = prep.target_reference if prep is not None else (source.value if source is not None and not is_local else None)
        return ok(ConfigurationRecoveryStep(
            provider=provider_name, operation=op, local=is_local, is_json_value=is_json,
            preparation=prep, target_reference=target_ref, source=source, state="Pending",
        ))

    def write_steps(self, steps: list[ConfigurationRecoveryStep], proposed: dict[str, ConfigurationValue]) -> Result[None]:
        """Execute all non-terminal Write steps and populate proposed entries."""
        for idx, step in enumerate(steps):
            if step.operation.action != "Write":
                continue
            if not self._is_terminal(step):
                run_res = self._run_step(steps, idx, reconcile=False)
                if not run_res.ok:
                    return run_res
            out_entry = steps[idx].output
            assert out_entry is not None
            proposed[step.operation.key] = out_entry
        return ok(None)

    def cleanup_steps(self, steps: list[ConfigurationRecoveryStep]) -> Result[None]:
        """Execute all non-terminal Remove steps."""
        for idx, step in enumerate(steps):
            if step.operation.action == "Remove" and not self._is_terminal(step):
                run_res = self._run_step(steps, idx, reconcile=False)
                if not run_res.ok:
                    return run_res
        return ok(None)

    def outcome(self, state: ConfigurationRecoveryState) -> ConfigurationRecoveryOutcome:
        """Compute the status, progress receipt, and optional recovery handle for state."""
        status = self._status(state)
        prog = self._progress(state, status)
        handle: ConfigurationRecoveryHandle | None = None
        if self._protector is not None and status == "Unfinished":
            sealed = self._protector.seal(state)
            if sealed.ok:
                handle = sealed.value
        return ConfigurationRecoveryOutcome(status=status, progress=prog, recovery_handle=handle)

    def attempt[T](self, result: Result[T], state: ConfigurationRecoveryState | None) -> ConfigurationAttempt[T]:
        """Wrap result and optional state into a ConfigurationAttempt."""
        if state is None:
            return ConfigurationAttempt(result=result)
        out = self.outcome(state)
        return ConfigurationAttempt(result=result, progress=out.progress, recovery_handle=out.recovery_handle)

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Authenticate handle, reconcile unknown remote mutations, and finish remaining steps."""
        if self._protector is None:
            return fail(_MANUAL_MSG, "Indeterminate")
        opened = self._protector.open(handle)
        if not opened.ok:
            return opened
        state = opened.value
        doc = self._documents(state.namespace)
        steps, proposed = list(state.steps), dict(state.proposed)
        publication, conflicted, pub_started = state.publication, state.conflicted, False

        def _mark_conflict() -> Result[bool]:
            nonlocal conflicted
            conflicted = True
            if steps:
                steps[0] = replace(steps[0], state="Conflict")
            return fail(_CONFLICT_MSG, "Conflict")

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            nonlocal publication, pub_started
            is_orig = self._matches(entries, state.original)
            is_prop = self._matches(entries, proposed) and all(self._is_terminal(s) for s in steps if s.operation.action == "Write")
            if publication == "Published" and not is_prop:
                return _mark_conflict()
            if is_prop and (not is_orig or publication == "Published"):
                publication = "Published"

            relevant = [s for s in steps if s.operation.action == "Remove"] if publication == "Published" else steps
            for s in relevant:
                if not s.local:
                    v_res = self._get_vault(s.provider)
                    if not v_res.ok or (
                        s.preparation is not None
                        and (v_res.value.recovery is None or v_res.value.recovery.store_identity != s.preparation.store_identity)
                    ):
                        return _mark_conflict()

            for idx, s in enumerate(steps):
                if not s.local and s.state != "Retained" and not (publication == "Published" and s.operation.action == "Write"):
                    rec_res = self._run_step(steps, idx, reconcile=True)
                    if not rec_res.ok:
                        return rec_res

            for s in steps:
                if s.operation.action == "Write" and s.output is not None:
                    proposed[s.operation.key] = s.output

            is_prop = self._matches(entries, proposed) and all(self._is_terminal(s) for s in steps if s.operation.action == "Write")
            if not is_orig and not is_prop:
                return _mark_conflict()
            if is_prop:
                publication = "Published"

            if publication != "Published":
                w_res = self.write_steps(steps, proposed)
                if not w_res.ok:
                    return w_res
                entries.clear()
                entries.update(proposed)
                pub_started, publication = True, "Unknown"
                return ok(True)

            c_res = self.cleanup_steps(steps)
            return ok(False) if c_res.ok else c_res

        mod_res = doc.modify(_mutator)
        if mod_res.ok:
            if pub_started:
                publication = "Published"
            c_res = self.cleanup_steps(steps)
            if not c_res.ok:
                mod_res = c_res
        elif pub_started:
            publication = "Failed"

        final_state = ConfigurationRecoveryState(
            operation_id=state.operation_id, namespace=state.namespace, kind=state.kind, original=state.original,
            proposed=proposed, steps=tuple(steps), publication=publication, conflicted=conflicted, version=state.version,
        )
        return ok(self.outcome(final_state))

    def _run_step(self, steps: list[ConfigurationRecoveryStep], index: int, reconcile: bool) -> Result[None]:
        step, req = steps[index], steps[index].operation
        if step.local:
            return self._run_local_step(steps, index)

        vault_res = self._get_vault(step.provider)
        if not vault_res.ok:
            return vault_res
        vault = vault_res.value

        if step.preparation is not None:
            cap = vault.recovery
            if cap is None or cap.store_identity != step.preparation.store_identity:
                steps[index] = replace(step, state="Conflict")
                return fail(_CONFLICT_MSG, "Conflict")
            steps[index] = replace(step, state="Unknown")
            eff_res = cap.reconcile(req, step.preparation) if reconcile else cap.execute(req, step.preparation)
            if not eff_res.ok:
                return eff_res
            eff = eff_res.value
            if reconcile and self._is_terminal(step) and eff.state == "Failed":
                steps[index] = replace(step, state="Manual")
                return fail(_MANUAL_MSG, "Indeterminate")
            output = step.output
            if eff.state == "Applied" and req.action == "Write":
                if eff.pointer is None or eff.pointer.provider != step.provider:
                    return fail(_UNFINISHED_MSG, "Unavailable")
                if output is not None and output.value != eff.pointer.value:
                    steps[index] = replace(step, state="Conflict")
                    return fail(_CONFLICT_MSG, "Conflict")
                output = ConfigurationValue(value=eff.pointer.value, provider=step.provider, encrypted=False, is_json_value=step.is_json_value)
            steps[index] = replace(step, state=eff.state, output=output)
            if eff.state in ("Applied", "Retained") or (reconcile and eff.state == "Failed"):
                return ok(None)
            if eff.state == "Conflict":
                return fail(_CONFLICT_MSG, "Conflict")
            return fail(_MANUAL_MSG, "Indeterminate") if eff.state == "Manual" else fail(_UNFINISHED_MSG, "Unavailable")

        if reconcile:
            steps[index] = replace(step, state="Manual")
            return fail(_MANUAL_MSG, "Indeterminate")
        steps[index] = replace(step, state="Unknown")
        if req.action == "Write":
            assert req.value is not None
            w_res = vault.write(req.namespace, req.key, req.value)
            if not w_res.ok:
                return w_res
            out_val = ConfigurationValue(value=w_res.value.value, provider=step.provider, encrypted=False, is_json_value=step.is_json_value)
            steps[index] = replace(step, state="Applied", output=out_val)
            return ok(None)

        assert step.source is not None
        r_res = vault.remove(ConfigurationPointer(provider=step.provider, value=step.source.value))
        if not r_res.ok:
            return r_res
        steps[index] = replace(step, state="Retained" if r_res.value else "Applied")
        return ok(None)

    def _run_local_step(self, steps: list[ConfigurationRecoveryStep], index: int) -> Result[None]:
        step, req = steps[index], steps[index].operation
        if req.action == "Remove":
            steps[index] = replace(step, state="Applied")
            return ok(None)
        assert req.value is not None
        if step.provider == "":
            steps[index] = replace(step, state="Applied", output=ConfigurationValue(value=req.value, is_json_value=step.is_json_value))
            return ok(None)
        if step.provider == "aesLocal":
            c_res = self._get_cipher()
            if not c_res.ok:
                steps[index] = replace(step, state="Failed")
                return c_res
            s_res = c_res.value.seal(req.value, req.namespace, req.key)
            if not s_res.ok:
                steps[index] = replace(step, state="Failed")
                return s_res
            out_val = ConfigurationValue(value=s_res.value, provider="aesLocal", encrypted=True, is_json_value=step.is_json_value)
            steps[index] = replace(step, state="Applied", output=out_val)
            return ok(None)
        steps[index] = replace(step, state="Failed")
        return fail(f"Unsupported local provider '{step.provider}'.", "Validation")

    @staticmethod
    def _is_terminal(step: ConfigurationRecoveryStep) -> bool:
        return step.state in ("Applied", "Retained")

    @staticmethod
    def _status(state: ConfigurationRecoveryState) -> ConfigurationRecoveryStatus:
        if state.conflicted:
            return "Conflict"
        relevant = [s for s in state.steps if s.operation.action == "Remove"] if state.publication == "Published" else list(state.steps)
        if any(s.state == "Conflict" for s in relevant):
            return "Conflict"
        if any(s.state == "Manual" or (not s.local and s.preparation is None and not ConfigurationRecoveryCoordinator._is_terminal(s)) for s in relevant):
            return "ManualReconciliation"
        if state.publication == "Published" and all(ConfigurationRecoveryCoordinator._is_terminal(s) for s in relevant):
            return "Completed"
        return "ManualReconciliation" if any(not s.local and s.preparation is None for s in relevant) else "Unfinished"

    @staticmethod
    def _progress(state: ConfigurationRecoveryState, status: ConfigurationRecoveryStatus) -> ConfigurationOperationProgress:
        return ConfigurationOperationProgress(
            operation_id=state.operation_id, namespace=state.namespace, kind=state.kind, publication=state.publication, recovery=status,
            steps=tuple(
                ConfigurationStepProgress(
                    operation_id=s.operation.operation_id, key=s.operation.key, provider=s.provider,
                    action=s.operation.action, state=s.state, target_reference=s.target_reference,
                )
                for s in state.steps
            ),
        )

    @staticmethod
    def _matches(actual: Mapping[str, ConfigurationValue], expected: Mapping[str, ConfigurationValue]) -> bool:
        if len(actual) != len(expected):
            return False
        for k, exp_v in expected.items():
            act_v = actual.get(k)
            if act_v is None:
                return False
            if act_v == exp_v:
                continue
            if not act_v.is_literal or not act_v.is_json_value or act_v.encrypted or replace(act_v, value=exp_v.value) != exp_v:
                return False
            try:
                if json.loads(act_v.value) != json.loads(exp_v.value):
                    return False
            except Exception:
                return False
        return True
