# Responsibility: Derive HKDF-SHA256 purpose subkeys and seal/authenticate ConfigurationRecoveryHandle tokens.
# Public interface:
# `-- ConfigurationRecoveryTokenProtector
#     +-- canonical_root(root)
#     +-- new(application_key, application_name, configuration_directory)
#     +-- open(handle)
#     `-- seal(state)
"""HKDF-SHA256 + AES-256-GCM recovery token protector matching .NET ConfigurationRecovery."""

import json
from pathlib import Path
import sys
from typing import Final

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

from nyssa_ai_configuration.cloud_key_vaults.types import (
    CloudKeyVaultAction,
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    ConfigurationPointer,
    ConfigurationStepState,
)
from nyssa_ai_configuration.encryption.configuration_cipher_aes_gcm import (
    ConfigurationCipherAesGcm,
)
from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationOperationKind,
    ConfigurationPublicationState,
    ConfigurationRecoveryState,
    ConfigurationRecoveryStep,
)
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_value_codec import (
    ConfigurationValueCodec,
)
from nyssa_ai_configuration.values.types import ConfigurationValue

_HKDF_INFO: Final[bytes] = b"NyssaAI.Configuration.Recovery/v1"
_INVALID_HANDLE_MSG: Final[str] = (
    "The recovery handle is invalid, unsupported, or belongs to a different application, configuration root, or protection key."
)
_KINDS: Final[tuple[ConfigurationOperationKind, ...]] = ("Move", "Replace", "Merge")
_PUBLICATIONS: Final[tuple[ConfigurationPublicationState, ...]] = ("NotAttempted", "Published", "Failed", "Unknown")
_ACTIONS: Final[tuple[CloudKeyVaultAction, ...]] = ("Write", "Remove")
_STEP_STATES: Final[tuple[ConfigurationStepState, ...]] = (
    "Pending",
    "Applied",
    "Failed",
    "Unknown",
    "Conflict",
    "Retained",
    "Manual",
)


class ConfigurationRecoveryTokenProtector:
    """Encrypts and authenticates recovery state with a purpose-specific HKDF-SHA256 subkey."""

    def __init__(
        self,
        application_key: bytes,
        application_name: str,
        configuration_directory: str | Path,
    ) -> None:
        """Derive a 32-byte HKDF-SHA256 subkey from application_key and bind to application_name and canonical root."""
        hkdf = HKDF(
            algorithm=hashes.SHA256(),
            length=32,
            salt=None,
            info=_HKDF_INFO,
        )
        derived = hkdf.derive(application_key)
        self._cipher = ConfigurationCipherAesGcm(derived)
        self._application = application_name
        self._root = self.canonical_root(configuration_directory)

    @staticmethod
    def canonical_root(root: str | Path) -> str:
        """Resolve symlinks, trim trailing separators, and uppercase on Windows to match .NET CanonicalRoot."""
        resolved = str(Path(str(root)).resolve()).rstrip("/\\")
        return resolved.upper() if sys.platform == "win32" else resolved

    def seal(self, state: ConfigurationRecoveryState) -> Result[ConfigurationRecoveryHandle]:
        """Serialize and seal a ConfigurationRecoveryState into a ConfigurationRecoveryHandle."""
        serialized = self._encode_state(state)
        if not serialized.ok:
            return serialized
        encrypted = self._cipher.seal(serialized.value, self._application, self._root)
        if not encrypted.ok:
            return encrypted
        return ConfigurationRecoveryHandle.import_token(encrypted.value)

    def open(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryState]:
        """Authenticate and decrypt a ConfigurationRecoveryHandle into a ConfigurationRecoveryState."""
        opened = self._cipher.open(handle.export(), self._application, self._root)
        if not opened.ok:
            return fail(_INVALID_HANDLE_MSG, "Validation")
        return self._decode_state(opened.value)

    @staticmethod
    def _encode_state(state: ConfigurationRecoveryState) -> Result[str]:
        orig_dict: dict[str, object] = {}
        for k, v in state.original.items():
            enc = ConfigurationValueCodec.encode_entry(v)
            if not enc.ok:
                return enc
            orig_dict[k] = enc.value

        prop_dict: dict[str, object] = {}
        for k, v in state.proposed.items():
            enc = ConfigurationValueCodec.encode_entry(v)
            if not enc.ok:
                return enc
            prop_dict[k] = enc.value

        steps_list: list[dict[str, object]] = []
        for s in state.steps:
            op_dict: dict[str, object] = {
                "OperationId": s.operation.operation_id,
                "Action": _ACTIONS.index(s.operation.action),
                "Namespace": s.operation.namespace,
                "Key": s.operation.key,
                "Value": s.operation.value,
                "Pointer": (
                    {"Provider": s.operation.pointer.provider, "Value": s.operation.pointer.value}
                    if s.operation.pointer is not None
                    else None
                ),
            }
            prep_dict: dict[str, object] | None = None
            if s.preparation is not None:
                prep_dict = {
                    "StoreIdentity": s.preparation.store_identity,
                    "TargetReference": s.preparation.target_reference,
                    "Precondition": s.preparation.precondition,
                }
            out_dict: dict[str, object] | None = None
            if s.output is not None:
                enc_out = ConfigurationValueCodec.encode_entry(s.output)
                if not enc_out.ok:
                    return enc_out
                out_dict = enc_out.value
            src_dict: dict[str, object] | None = None
            if s.source is not None:
                enc_src = ConfigurationValueCodec.encode_entry(s.source)
                if not enc_src.ok:
                    return enc_src
                src_dict = enc_src.value
            steps_list.append(
                {
                    "Provider": s.provider,
                    "Operation": op_dict,
                    "Local": s.local,
                    "IsJsonValue": s.is_json_value,
                    "Preparation": prep_dict,
                    "TargetReference": s.target_reference,
                    "Output": out_dict,
                    "Source": src_dict,
                    "State": _STEP_STATES.index(s.state),
                }
            )

        payload = {
            "Conflicted": state.conflicted,
            "Version": state.version,
            "OperationId": state.operation_id,
            "Namespace": state.namespace,
            "Kind": _KINDS.index(state.kind),
            "Original": orig_dict,
            "Proposed": prop_dict,
            "Steps": steps_list,
            "Publication": _PUBLICATIONS.index(state.publication),
        }
        return ok(json.dumps(payload, separators=(",", ":")))

    @staticmethod
    def _decode_state(raw_json: str) -> Result[ConfigurationRecoveryState]:
        try:
            parsed: object = json.loads(raw_json)
        except Exception as exc:
            return fail(_INVALID_HANDLE_MSG, "Validation", exc)
        if not isinstance(parsed, dict) or parsed.get("Version", parsed.get("version")) != 1:
            return fail(_INVALID_HANDLE_MSG, "Validation")

        orig_raw = parsed.get("Original", parsed.get("original"))
        prop_raw = parsed.get("Proposed", parsed.get("proposed"))
        steps_raw = parsed.get("Steps", parsed.get("steps"))
        op_id = parsed.get("OperationId", parsed.get("operationId"))
        ns = parsed.get("Namespace", parsed.get("namespace"))
        kind_raw = parsed.get("Kind", parsed.get("kind"))
        pub_raw = parsed.get("Publication", parsed.get("publication", 0))
        kind = _KINDS[kind_raw] if isinstance(kind_raw, int) and 0 <= kind_raw < len(_KINDS) else kind_raw
        pub = _PUBLICATIONS[pub_raw] if isinstance(pub_raw, int) and 0 <= pub_raw < len(_PUBLICATIONS) else pub_raw
        if (
            not isinstance(orig_raw, dict)
            or not isinstance(prop_raw, dict)
            or not isinstance(steps_raw, list)
            or not isinstance(op_id, str)
            or not isinstance(ns, str)
            or kind not in _KINDS
            or pub not in _PUBLICATIONS
        ):
            return fail(_INVALID_HANDLE_MSG, "Validation")

        orig_map: dict[str, ConfigurationValue] = {}
        for k, v in orig_raw.items():
            if not isinstance(k, str):
                return fail(_INVALID_HANDLE_MSG, "Validation")
            dec = ConfigurationValueCodec.decode_entry(v)
            if not dec.ok:
                return fail(_INVALID_HANDLE_MSG, "Validation")
            orig_map[k] = dec.value

        prop_map: dict[str, ConfigurationValue] = {}
        for k, v in prop_raw.items():
            if not isinstance(k, str):
                return fail(_INVALID_HANDLE_MSG, "Validation")
            dec = ConfigurationValueCodec.decode_entry(v)
            if not dec.ok:
                return fail(_INVALID_HANDLE_MSG, "Validation")
            prop_map[k] = dec.value

        steps_list: list[ConfigurationRecoveryStep] = []
        for item in steps_raw:
            step_res = ConfigurationRecoveryTokenProtector._decode_step(item)
            if not step_res.ok:
                return fail(_INVALID_HANDLE_MSG, "Validation")
            steps_list.append(step_res.value)

        return ok(
            ConfigurationRecoveryState(
                operation_id=op_id,
                namespace=ns,
                kind=kind,
                original=orig_map,
                proposed=prop_map,
                steps=tuple(steps_list),
                publication=pub,
                conflicted=bool(parsed.get("Conflicted", parsed.get("conflicted", False))),
                version=1,
            )
        )

    @staticmethod
    def _decode_step(item: object) -> Result[ConfigurationRecoveryStep]:
        if not isinstance(item, dict):
            return fail(_INVALID_HANDLE_MSG, "Validation")
        op_raw = item.get("Operation", item.get("operation"))
        if not isinstance(op_raw, dict):
            return fail(_INVALID_HANDLE_MSG, "Validation")
        ptr_raw = op_raw.get("Pointer", op_raw.get("pointer"))
        ptr: ConfigurationPointer | None = None
        if isinstance(ptr_raw, dict):
            ptr = ConfigurationPointer(
                provider=str(ptr_raw.get("Provider", ptr_raw.get("provider", ""))),
                value=str(ptr_raw.get("Value", ptr_raw.get("value", ""))),
            )
        act_raw = op_raw.get("Action", op_raw.get("action", 0))
        act: CloudKeyVaultAction = (
            _ACTIONS[act_raw]
            if isinstance(act_raw, int) and 0 <= act_raw < len(_ACTIONS)
            else ("Remove" if act_raw == "Remove" else "Write")
        )
        val_raw = op_raw.get("Value", op_raw.get("value"))
        op = CloudKeyVaultOperation(
            operation_id=str(op_raw.get("OperationId", op_raw.get("operationId", ""))),
            action=act,
            namespace=str(op_raw.get("Namespace", op_raw.get("namespace", ""))),
            key=str(op_raw.get("Key", op_raw.get("key", ""))),
            value=str(val_raw) if isinstance(val_raw, str) else None,
            pointer=ptr,
        )
        prep_raw = item.get("Preparation", item.get("preparation"))
        prep: CloudKeyVaultPreparation | None = None
        if isinstance(prep_raw, dict):
            t_ref = prep_raw.get("TargetReference", prep_raw.get("targetReference"))
            prec = prep_raw.get("Precondition", prep_raw.get("precondition"))
            prep = CloudKeyVaultPreparation(
                store_identity=str(prep_raw.get("StoreIdentity", prep_raw.get("storeIdentity", ""))),
                target_reference=str(t_ref) if isinstance(t_ref, str) else None,
                precondition=str(prec) if isinstance(prec, str) else None,
            )
        out_raw = item.get("Output", item.get("output"))
        out_val: ConfigurationValue | None = None
        if out_raw is not None:
            dec_out = ConfigurationValueCodec.decode_entry(out_raw)
            if not dec_out.ok:
                return dec_out
            out_val = dec_out.value
        src_raw = item.get("Source", item.get("source"))
        src_val: ConfigurationValue | None = None
        if src_raw is not None:
            dec_src = ConfigurationValueCodec.decode_entry(src_raw)
            if not dec_src.ok:
                return dec_src
            src_val = dec_src.value
        st_raw = item.get("State", item.get("state", 0))
        st: ConfigurationStepState = (
            _STEP_STATES[st_raw]
            if isinstance(st_raw, int) and 0 <= st_raw < len(_STEP_STATES)
            else (st_raw if st_raw in _STEP_STATES else "Pending")
        )
        step_tref = item.get("TargetReference", item.get("targetReference"))
        return ok(
            ConfigurationRecoveryStep(
                provider=str(item.get("Provider", item.get("provider", ""))),
                operation=op,
                local=bool(item.get("Local", item.get("local", False))),
                is_json_value=bool(item.get("IsJsonValue", item.get("isJsonValue", False))),
                preparation=prep,
                target_reference=str(step_tref) if isinstance(step_tref, str) else None,
                output=out_val,
                source=src_val,
                state=st,
            )
        )
