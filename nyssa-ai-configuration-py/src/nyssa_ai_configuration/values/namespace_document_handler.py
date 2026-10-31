# Responsibility: Read, replace, merge, and migrate full-namespace JSON documents with legacy namespaceSettings support.
# Public interface:
# `-- NamespaceDocumentHandler
#     +-- get_namespace(namespace)
#     +-- new(document_factory, read_through, check_destination, recovery_coordinator)
#     `-- set_namespace(namespace, serialized, replace)
"""Full-namespace JSON document handler matching .NET NamespaceDocumentHandler."""

from collections.abc import Callable
from dataclasses import replace as dc_replace
import json
from typing import Final
import uuid

from nyssa_ai_configuration.recovery.configuration_recovery_coordinator import (
    ConfigurationRecoveryCoordinator,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationOperationKind,
    ConfigurationPublicationState,
    ConfigurationRecoveryState,
    ConfigurationRecoveryStep,
)
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_document_local_file import (
    ConfigurationDocumentLocalFile,
)
from nyssa_ai_configuration.values.configuration_value_codec import (
    ConfigurationValueCodec,
)
from nyssa_ai_configuration.values.types import ConfigurationValue

_LEGACY_SETTINGS_KEY: Final[str] = "namespaceSettings"


class NamespaceDocumentHandler:
    """Manages reading, writing, merging, and legacy migration of full-namespace JSON documents."""

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

    def get_namespace(self, namespace: str) -> Result[str]:
        """Read and assemble all entries in <namespace>.json into a single JSON string."""
        doc = self._documents(namespace)
        read_res = doc.read()
        if not read_res.ok:
            return read_res
        entries = read_res.value
        if len(entries) == 0:
            return fail(f"No configuration entries found in namespace '{namespace}'.", "NotFound")

        if len(entries) == 1 and _LEGACY_SETTINGS_KEY in entries:
            return self._resolve_entry(entries[_LEGACY_SETTINGS_KEY], namespace, _LEGACY_SETTINGS_KEY)

        assembled: dict[str, object] = {}
        if _LEGACY_SETTINGS_KEY in entries:
            legacy_res = self._resolve_entry(entries[_LEGACY_SETTINGS_KEY], namespace, _LEGACY_SETTINGS_KEY)
            if not legacy_res.ok:
                return legacy_res
            try:
                legacy_obj: object = json.loads(legacy_res.value)
            except Exception as exc:
                return fail("The namespace settings could not be processed.", "Validation", exc)
            if isinstance(legacy_obj, dict):
                assembled.update(legacy_obj)

        for key, entry in entries.items():
            if key == _LEGACY_SETTINGS_KEY:
                continue
            resolved = self._resolve_entry(entry, namespace, key)
            if not resolved.ok:
                return resolved
            if entry.is_json_value:
                try:
                    assembled[key] = json.loads(resolved.value)
                except Exception as exc:
                    return fail(
                        "The namespace settings could not be processed. Namespace settings require valid JSON properties.",
                        "Validation",
                        exc,
                    )
            else:
                assembled[key] = resolved.value
        return ok(json.dumps(assembled, separators=(",", ":")))

    def set_namespace(
        self,
        namespace: str,
        serialized: str,
        replace: bool,
    ) -> ConfigurationAttempt[None]:
        """Replace or shallow-merge top-level properties in <namespace>.json while preserving recorded providers."""
        try:
            parsed: object = json.loads(serialized)
        except Exception as exc:
            return ConfigurationAttempt(
                result=fail(
                    "The namespace settings could not be processed. Namespace settings require valid JSON.",
                    "Validation",
                    exc,
                )
            )

        is_object = isinstance(parsed, dict)
        if is_object:
            assert isinstance(parsed, dict)
            props_res = self._read_namespace_properties(parsed)
            if not props_res.ok:
                return ConfigurationAttempt(result=props_res)
            incoming = props_res.value
        else:
            if not replace:
                return ConfigurationAttempt(
                    result=fail(
                        "The namespace settings could not be merged. Merging namespace settings requires JSON objects.",
                        "Validation",
                    )
                )
            incoming = {
                _LEGACY_SETTINGS_KEY: ConfigurationValue(value=serialized, is_json_value=not isinstance(parsed, str))
            }

        doc = self._documents(namespace)
        state_box: list[ConfigurationRecoveryState] = []
        publishing = False

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            nonlocal publishing
            original = dict(entries)
            if is_object and _LEGACY_SETTINGS_KEY in entries:
                legacy = entries[_LEGACY_SETTINGS_KEY]
                if not legacy.is_literal:
                    return fail(
                        f"Key '{namespace}/{_LEGACY_SETTINGS_KEY}' is already stored in provider '{legacy.provider}'.",
                        "Conflict",
                    )
                if not replace:
                    try:
                        legacy_parsed: object = json.loads(legacy.value)
                    except Exception as exc:
                        return fail("Legacy namespaceSettings is not valid JSON.", "Validation", exc)
                    if not isinstance(legacy_parsed, dict):
                        return fail(
                            "Merging namespace settings requires JSON objects for any legacy namespaceSettings value.",
                            "Validation",
                        )
                    legacy_props = self._read_namespace_properties(legacy_parsed)
                    if not legacy_props.ok:
                        return legacy_props
                    for lk, lv in legacy_props.value.items():
                        entries.setdefault(lk, lv)

            for key in incoming:
                existing = entries.get(key)
                if existing is None:
                    continue
                valid_entry = ConfigurationValueCodec.validate_entry(existing)
                if not valid_entry.ok:
                    return valid_entry
                if not existing.is_literal:
                    dest_ok = self._check_destination(existing.provider, namespace, key)
                    if not dest_ok.ok:
                        return dest_ok

            steps: list[ConfigurationRecoveryStep] = []
            for key, val in incoming.items():
                existing = entries.get(key)
                step_res = self._recovery.prepare_step(
                    provider_name=existing.provider if existing is not None else "",
                    action="Write",
                    namespace=namespace,
                    key=key,
                    value=val.value,
                    source=existing,
                    is_json=val.is_json_value,
                )
                if not step_res.ok:
                    return step_res
                steps.append(step_res.value)

            proposed = dict(entries)
            if replace:
                for k in list(proposed.keys()):
                    if k not in incoming:
                        del proposed[k]
            else:
                proposed.pop(_LEGACY_SETTINGS_KEY, None)

            op_id = str(uuid.uuid4())
            kind: ConfigurationOperationKind = "Replace" if replace else "Merge"
            written = self._recovery.write_steps(steps, proposed)
            cur_state = ConfigurationRecoveryState(
                operation_id=op_id,
                namespace=namespace,
                kind=kind,
                original=original,
                proposed=dict(proposed),
                steps=tuple(steps),
                publication="NotAttempted",
            )
            state_box.append(cur_state)
            if not written.ok:
                return written

            entries.clear()
            entries.update(proposed)
            publishing = True
            state_box[0] = dc_replace(state_box[0], publication="Unknown")
            return ok(True)

        mod_res = doc.modify(_mutator)
        final_state: ConfigurationRecoveryState | None = state_box[0] if state_box else None
        if final_state is not None:
            pub_state: ConfigurationPublicationState = (
                "Published" if mod_res.ok else ("Failed" if publishing else "NotAttempted")
            )
            final_state = dc_replace(final_state, publication=pub_state)

        if mod_res.ok:
            return self._recovery.attempt(ok(None), final_state)
        return self._recovery.attempt(mod_res, final_state)

    def _resolve_entry(self, entry: ConfigurationValue, namespace: str, key: str) -> Result[str]:
        valid = ConfigurationValueCodec.validate_entry(entry)
        if not valid.ok:
            return valid
        if entry.is_literal:
            return ok(entry.value)
        return self._read_through(entry, namespace, key)

    @staticmethod
    def _read_namespace_properties(raw_obj: dict[object, object]) -> Result[dict[str, ConfigurationValue]]:
        props: dict[str, ConfigurationValue] = {}
        for k, v in raw_obj.items():
            if not isinstance(k, str) or not k.strip() or k == _LEGACY_SETTINGS_KEY or k in props:
                return fail(
                    "The namespace settings could not be processed. Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
                    "Validation",
                )
            is_json = not isinstance(v, str)
            str_val = json.dumps(v, separators=(",", ":")) if not isinstance(v, str) else v
            props[k] = ConfigurationValue(value=str_val, is_json_value=is_json)
        return ok(props)
