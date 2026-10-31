# Responsibility: Migrate a single configuration entry between storage providers with guarded two-phase recovery.
# Public interface:
# `-- ConfigurationMover
#     +-- move(namespace, key, destination_provider)
#     `-- new(document_factory, read_through, check_destination, recovery_coordinator)
"""Single-key provider migration handler implementing F-201 and F-204."""

from collections.abc import Callable
from dataclasses import replace as dc_replace
import uuid

from nyssa_ai_configuration.recovery.configuration_recovery_coordinator import (
    ConfigurationRecoveryCoordinator,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationMove,
    ConfigurationPublicationState,
    ConfigurationRecoveryState,
    ConfigurationRecoveryStep,
)
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_document_local_file import (
    ConfigurationDocumentLocalFile,
)
from nyssa_ai_configuration.values.types import ConfigurationValue


class ConfigurationMover:
    """Migrates an existing configuration entry to a destination provider safely under lock."""

    def __init__(
        self,
        document_factory: Callable[[str], ConfigurationDocumentLocalFile],
        read_through: Callable[[ConfigurationValue, str, str], Result[str]],
        check_destination: Callable[[str, str, str], Result[None]],
        recovery_coordinator: ConfigurationRecoveryCoordinator,
    ) -> None:
        """Initialize with document factory, provider read-through callback, destination validator, and recovery coordinator."""
        self._documents = document_factory
        self._read_through = read_through
        self._check_destination = check_destination
        self._recovery = recovery_coordinator

    def move(
        self,
        namespace: str,
        key: str,
        destination_provider: str,
    ) -> ConfigurationAttempt[ConfigurationMove]:
        """Read cleartext from source provider, write to destination first, update <namespace>.json, then delete source."""
        if destination_provider == "oauth":
            return ConfigurationAttempt(
                result=fail("Cannot move an entry to the 'oauth' provider.", "Validation")
            )

        doc = self._documents(namespace)
        state_box: list[ConfigurationRecoveryState] = []
        publishing = False

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            nonlocal publishing
            existing = entries.get(key)
            if existing is None:
                return fail(f"Configuration entry '{namespace}/{key}' was not found.", "NotFound")
            if existing.provider == "oauth":
                return fail("OAuth entries cannot be moved between providers.", "Conflict")
            if existing.provider == destination_provider:
                return ok(False)

            dest_ok = self._check_destination(destination_provider, namespace, key)
            if not dest_ok.ok:
                return dest_ok

            cleartext_res = (
                ok(existing.value) if existing.is_literal else self._read_through(existing, namespace, key)
            )
            if not cleartext_res.ok:
                return cleartext_res

            w_step = self._recovery.prepare_step(
                provider_name=destination_provider,
                action="Write",
                namespace=namespace,
                key=key,
                value=cleartext_res.value,
                source=None,
                is_json=existing.is_json_value,
            )
            if not w_step.ok:
                return w_step

            steps: list[ConfigurationRecoveryStep] = [w_step.value]
            if existing.provider not in ("", "aesLocal"):
                r_step = self._recovery.prepare_step(
                    provider_name=existing.provider,
                    action="Remove",
                    namespace=namespace,
                    key=key,
                    value=None,
                    source=existing,
                    is_json=existing.is_json_value,
                )
                if not r_step.ok:
                    return r_step
                steps.append(r_step.value)

            original = dict(entries)
            proposed = dict(entries)
            w_res = self._recovery.write_steps(steps, proposed)
            cur_state = ConfigurationRecoveryState(
                operation_id=str(uuid.uuid4()),
                namespace=namespace,
                kind="Move",
                original=original,
                proposed=dict(proposed),
                steps=tuple(steps),
                publication="NotAttempted",
            )
            state_box.append(cur_state)
            if not w_res.ok:
                return w_res

            entries[key] = proposed[key]
            publishing = True
            state_box[0] = dc_replace(state_box[0], publication="Unknown")
            return ok(True)

        mod_res = doc.modify(_mutator)
        if not state_box:
            if mod_res.ok:
                return ConfigurationAttempt(result=ok(ConfigurationMove(source_value_remains=False)))
            return ConfigurationAttempt(result=mod_res)

        cur_state = state_box[0]
        if not mod_res.ok:
            pub_state: ConfigurationPublicationState = "Failed" if publishing else "NotAttempted"
            cur_state = dc_replace(cur_state, publication=pub_state)
            return self._recovery.attempt(mod_res, cur_state)

        steps_mut = list(cur_state.steps)
        clean_res = self._recovery.cleanup_steps(steps_mut)
        cur_state = dc_replace(cur_state, steps=tuple(steps_mut), publication="Published")
        out = self._recovery.outcome(cur_state)
        rem_steps = [s for s in steps_mut if s.operation.action == "Remove"]
        source_remains = any(s.state != "Applied" for s in rem_steps)
        del_err = clean_res.message if not clean_res.ok else None
        move_val = ConfigurationMove(
            source_value_remains=source_remains,
            source_delete_error=del_err,
            progress=out.progress if source_remains else None,
            recovery_handle=out.recovery_handle,
        )
        return ConfigurationAttempt(
            result=ok(move_val),
            progress=move_val.progress,
            recovery_handle=move_val.recovery_handle,
        )
