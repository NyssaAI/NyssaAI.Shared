"""Validate, inspect status, or sync a repository-owned standards copy and run local commands."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys

LANGUAGE = 'csharp'
PACKAGE = Path(__file__).resolve().parents[1]
EXCLUDED = {
    '.temp', 'node_modules', 'bin', 'obj', 'dist', '__pycache__',
    '.git', '.mypy_cache', '.pytest_cache', '.ruff_cache'
}
BANNED_ROOT_DIRS = {
    'contracts', 'shared', 'utils', 'helpers', 'common', 'misc', 'types', 'models',
    'tmp', 'temp', 'scratch', '.scratch', 'tests', '__tests__'
}
BANNED_GITIGNORE_TARGETS = {
    '.nyssa-ai', '.nyssa-ai/dev-standards', '.nyssa-ai/plan-product',
    '.compound-engineering', '.compound-engineering/config.yaml',
    'docs', 'docs/plans', 'docs/solutions', 'docs/concepts', 'docs/features',
    'AGENTS.md', 'CLAUDE.md', 'GEMINI.md'
}
HOST_DELEGATE_FILES = ('CLAUDE.md', 'GEMINI.md', '.hermes.md', 'HERMES.md', 'AGENTS.override.md')
IDE_ADAPTER_FILES = ('.cursorrules', '.github/copilot-instructions.md')
CANONICAL_INSTRUCTION_CASING = {'agents.md': 'AGENTS.md', 'claude.md': 'CLAUDE.md', 'gemini.md': 'GEMINI.md', 'hermes.md': 'HERMES.md'}
TEST_FILE_PATTERN = re.compile(r'^.*Tests\.cs$')


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def authored_files(directory):
    return sorted(
        p for p in directory.rglob('*')
        if not EXCLUDED.intersection(p.relative_to(directory).parts) and p.is_file()
    )


def resolve_plugin_root(explicit_source=None):
    candidates = []
    if explicit_source is not None:
        candidates.append(Path(explicit_source).expanduser().resolve())
    candidates.extend([
        Path(__file__).resolve().parents[2],
        Path.home() / '.gemini/config/plugins/dev-standards',
        Path.home() / '.claude/plugins/dev-standards',
        Path.home() / '.codex/plugins/dev-standards',
        Path.home() / '.cursor/plugins/dev-standards',
        Path.home() / '.agents/plugins/dev-standards',
    ])
    for candidate in candidates:
        if (
            (candidate / f'skills/dev-standards/rules/{LANGUAGE}-standards.md').is_file()
            and (candidate / f'skills/dev-standards/references/{LANGUAGE}/README.md').is_file()
            and (candidate / f'src/{LANGUAGE}/check_adoption.py').is_file()
        ):
            return candidate
    raise ValueError('unable to locate dev-standards plugin source; pass --source /path/to/dev-standards')


def build_bundle_entries(plugin_root):
    references = plugin_root / f'skills/dev-standards/references/{LANGUAGE}'
    rules_dir = plugin_root / 'skills/dev-standards/rules'
    src_dir = plugin_root / f'src/{LANGUAGE}'
    entries = {}

    for file in authored_files(references):
        relative = file.relative_to(references)
        if relative == Path('README.md'):
            arcname = 'README.md'
            text = file.read_text(encoding='utf-8')
            content = (
                text.replace('../../rules/repo-standards.md', 'rules/repo-standards.md')
                .replace('../../rules/architecture-standards.md', 'rules/architecture-standards.md')
                .replace(f'../../rules/{LANGUAGE}-standards.md', 'rules/standards.md')
                .replace(f'src/{LANGUAGE}/', 'scripts/')
                .encode('utf-8')
            )
        elif relative.parts[0] in {'adoption', 'examples'}:
            arcname = relative.as_posix()
            content = file.read_bytes()
        else:
            arcname = (Path('references') / relative).as_posix()
            content = file.read_bytes()
        entries[arcname] = content

    entries['rules/repo-standards.md'] = (rules_dir / 'repo-standards.md').read_bytes()
    entries['rules/architecture-standards.md'] = (rules_dir / 'architecture-standards.md').read_bytes()
    entries['rules/standards.md'] = (rules_dir / f'{LANGUAGE}-standards.md').read_bytes()
    for file in authored_files(src_dir):
        arcname = (Path('scripts') / file.relative_to(src_dir)).as_posix()
        entries[arcname] = file.read_bytes()
    return entries


def resolve_revision(plugin_root, entries, explicit_revision=None):
    if explicit_revision is not None:
        if not re.fullmatch(r'[0-9a-fA-F]{40}', explicit_revision):
            raise ValueError('revision must name the exact 40-character source commit')
        return explicit_revision.lower()
    if (plugin_root / '.git').exists() and shutil.which('git'):
        proc = subprocess.run(
            ['git', '-C', str(plugin_root), 'rev-parse', '--verify', 'HEAD'],
            capture_output=True,
            text=True,
        )
        sha = proc.stdout.strip()
        if proc.returncode == 0 and re.fullmatch(r'[0-9a-fA-F]{40}', sha):
            return sha.lower()
    digest = hashlib.sha1()
    for arcname, content in sorted(entries.items()):
        digest.update(arcname.encode('utf-8') + b'\0' + content + b'\0')
    return digest.hexdigest()


def extract_agents_section(text):
    heading_pattern = re.compile(rf'(?ms)^# Development standards \({re.escape(LANGUAGE)}\)[ \t]*\r?\n.*?(?=^# |\Z)')
    match = heading_pattern.search(text)
    return match.group(0).strip() + '\n' if match else None


def normalize_instruction_casing(root):
    if not root.is_dir():
        return
    existing_names = {child.name: child for child in root.iterdir() if child.is_file() or child.is_symlink()}
    for lower_name, canonical_name in CANONICAL_INSTRUCTION_CASING.items():
        if lower_name in existing_names and canonical_name not in existing_names:
            src_file = existing_names[lower_name]
            temp_file = root / f'.tmp_{canonical_name}'
            src_file.rename(temp_file)
            temp_file.rename(root / canonical_name)


def inspect_instruction_files(root, config):
    statuses = []
    modifications = []
    existing_names = {child.name: child for child in root.iterdir() if child.is_file() or child.is_symlink()} if root.is_dir() else {}

    agents_path = root / 'AGENTS.md'
    has_agents = 'AGENTS.md' in existing_names or any(k.lower() == 'agents.md' for k in existing_names)
    agents_text = agents_path.read_text(encoding='utf-8') if has_agents and agents_path.is_file() else None
    recorded_agents_hash = config.get('agents_section_hash')

    if agents_text is not None:
        section = extract_agents_section(agents_text)
        if section is None:
            statuses.append('AGENTS.md (missing standards section)')
            if isinstance(recorded_agents_hash, str) and recorded_agents_hash:
                modifications.append(f'deleted: AGENTS.md (# Development standards ({LANGUAGE}))')
        elif isinstance(recorded_agents_hash, str) and recorded_agents_hash and sha256_bytes(section.encode('utf-8')) != recorded_agents_hash:
            statuses.append('AGENTS.md (modified standards section)')
            modifications.append(f'modified: AGENTS.md (# Development standards ({LANGUAGE}))')
        else:
            statuses.append('AGENTS.md (synced)')
    else:
        statuses.append('AGENTS.md (absent -> will create)')
        if isinstance(recorded_agents_hash, str) and recorded_agents_hash:
            modifications.append('deleted: AGENTS.md')

    for host_file in HOST_DELEGATE_FILES:
        candidate = root / host_file
        present = host_file in existing_names or any(k.lower() == host_file.lower() for k in existing_names)
        if present and candidate.is_symlink():
            statuses.append(f'{host_file} (symlink -> AGENTS.md)')
        elif present and candidate.is_file():
            body = candidate.read_text(encoding='utf-8').strip()
            if body in {'@./AGENTS.md', '@AGENTS.md'}:
                statuses.append(f'{host_file} (@./AGENTS.md)')
            elif agents_text is not None:
                if body == agents_text.strip():
                    statuses.append(f'{host_file} (mirror of AGENTS.md)')
                else:
                    statuses.append(f'{host_file} (diverged from AGENTS.md)')
                    modifications.append(f'modified: {host_file} (diverged from AGENTS.md)')
            else:
                statuses.append(f'{host_file} (standalone -> will migrate to AGENTS.md)')
        elif host_file == 'CLAUDE.md':
            statuses.append('CLAUDE.md (absent -> will create @./AGENTS.md)')

    for adapter_file in IDE_ADAPTER_FILES:
        candidate = root / adapter_file
        if candidate.is_symlink():
            statuses.append(f'{adapter_file} (symlink -> AGENTS.md)')
        elif candidate.is_file():
            body = candidate.read_text(encoding='utf-8').strip()
            if body in {'@./AGENTS.md', '@AGENTS.md'}:
                statuses.append(f'{adapter_file} (@./AGENTS.md)')
            else:
                section = extract_agents_section(body)
                if section is not None and isinstance(recorded_agents_hash, str) and recorded_agents_hash and sha256_bytes(section.encode('utf-8')) != recorded_agents_hash:
                    statuses.append(f'{adapter_file} (modified standards section)')
                    modifications.append(f'modified: {adapter_file} (# Development standards ({LANGUAGE}))')
                else:
                    statuses.append(f'{adapter_file} (present)')

    return statuses, modifications


def detect_local_version(bundle_dir, config):
    if isinstance(config.get('version'), str) and config['version'].strip():
        return config['version'].strip()
    readme = bundle_dir / 'README.md'
    if readme.is_file():
        match = re.search(r'^#\s+.+?\s+standards\s+—\s+([0-9]+\.[0-9]+\.[0-9]+)', readme.read_text(encoding='utf-8'), re.M)
        if match:
            return match.group(1)
    return 'unknown'


def detect_local_modifications(root, bundle_dir, config, entries, global_version, global_revision, instruction_modifications=None):
    modifications = []
    if bundle_dir.is_dir():
        current_files = {
            p.relative_to(bundle_dir).as_posix(): p.read_bytes()
            for p in authored_files(bundle_dir)
        }
        recorded_hashes = config.get('file_hashes')
        if isinstance(recorded_hashes, dict) and recorded_hashes:
            for rel, content in sorted(current_files.items()):
                digest = sha256_bytes(content)
                if rel not in recorded_hashes:
                    modifications.append(f'added: .nyssa-ai/dev-standards/{LANGUAGE}/{rel}')
                elif recorded_hashes[rel] != digest:
                    modifications.append(f'modified: .nyssa-ai/dev-standards/{LANGUAGE}/{rel}')
            for rel in sorted(recorded_hashes):
                if rel not in current_files:
                    modifications.append(f'deleted: .nyssa-ai/dev-standards/{LANGUAGE}/{rel}')
        else:
            local_version = detect_local_version(bundle_dir, config)
            local_revision = str(config.get('revision', ''))
            if local_version == global_version or local_revision == global_revision:
                for rel, content in sorted(current_files.items()):
                    if rel not in entries:
                        modifications.append(f'added: .nyssa-ai/dev-standards/{LANGUAGE}/{rel}')
                    elif entries[rel] != content:
                        modifications.append(f'modified: .nyssa-ai/dev-standards/{LANGUAGE}/{rel}')
            elif (root / '.git').exists() and shutil.which('git'):
                rel_bundle = f'.nyssa-ai/dev-standards/{LANGUAGE}'
                status_proc = subprocess.run(
                    ['git', '-C', str(root), 'status', '--porcelain', '--', rel_bundle],
                    capture_output=True,
                    text=True,
                )
                for line in status_proc.stdout.splitlines():
                    if line.strip():
                        path_part = line[3:].strip()
                        modifications.append(f'uncommitted ({line[:2].strip()}): {path_part}')
                pin_proc = subprocess.run(
                    ['git', '-C', str(root), 'log', '-n', '1', '--format=%H', '--', f'.nyssa-ai/dev-standards/{LANGUAGE}.json'],
                    capture_output=True,
                    text=True,
                )
                pin_sha = pin_proc.stdout.strip()
                if pin_sha:
                    diff_proc = subprocess.run(
                        ['git', '-C', str(root), 'diff', '--name-status', f'{pin_sha}..HEAD', '--', rel_bundle],
                        capture_output=True,
                        text=True,
                    )
                    for line in diff_proc.stdout.splitlines():
                        parts = line.split(maxsplit=1)
                        if len(parts) == 2:
                            entry = f'committed after pin ({parts[0]}): {parts[1]}'
                            if entry not in modifications:
                                modifications.append(entry)
            if 'scripts/grouped-dtos.json' in current_files and 'scripts/grouped-dtos.json' in entries:
                if current_files['scripts/grouped-dtos.json'] != entries['scripts/grouped-dtos.json']:
                    entry = f'modified: .nyssa-ai/dev-standards/{LANGUAGE}/scripts/grouped-dtos.json'
                    if entry not in modifications:
                        modifications.append(entry)

    if instruction_modifications is None:
        _, instruction_modifications = inspect_instruction_files(root, config)
    for item in instruction_modifications:
        if item not in modifications:
            modifications.append(item)

    return modifications


def inspect_status(root, explicit_source=None, explicit_revision=None):
    plugin_root = resolve_plugin_root(explicit_source)
    plugin_manifest = json.loads((plugin_root / 'plugin.json').read_text(encoding='utf-8'))
    global_version = str(plugin_manifest.get('version', 'unknown'))
    entries = build_bundle_entries(plugin_root)
    global_revision = resolve_revision(plugin_root, entries, explicit_revision)

    bundle_dir = root / '.nyssa-ai' / 'dev-standards' / LANGUAGE
    config_path = root / '.nyssa-ai' / 'dev-standards' / f'{LANGUAGE}.json'
    installed = bundle_dir.is_dir() or config_path.is_file()
    config = json.loads(config_path.read_text(encoding='utf-8')) if config_path.is_file() else {}
    local_version = detect_local_version(bundle_dir, config) if installed else 'not installed'
    local_revision = str(config.get('revision', 'none')) if installed else 'none'
    instruction_files, instruction_modifications = inspect_instruction_files(root, config)
    modifications = detect_local_modifications(
        root, bundle_dir, config, entries, global_version, global_revision, instruction_modifications
    )
    return {
        'language': LANGUAGE,
        'plugin_root': plugin_root,
        'entries': entries,
        'global_version': global_version,
        'global_revision': global_revision,
        'installed': installed,
        'local_version': local_version,
        'local_revision': local_revision,
        'instruction_files': instruction_files,
        'modifications': modifications,
    }


def print_status_banner(info):
    print('=== Workflow: Adopt or Update Local Repository Standards (sync-standards) ===')
    print(f'Language            : {info["language"]}')
    print(f'Global Instance     : v{info["global_version"]} (revision {info["global_revision"][:12]}... at {info["plugin_root"]})')
    if info['installed']:
        rev_short = info['local_revision'][:12] + '...' if len(info['local_revision']) >= 12 else info['local_revision']
        print(f'Local Instance      : v{info["local_version"]} (revision {rev_short} at .nyssa-ai/dev-standards/{info["language"]})')
    else:
        print(f'Local Instance      : Not installed (.nyssa-ai/dev-standards/{info["language"]} absent)')
    print(f'Instruction Files   : {", ".join(info["instruction_files"])}')
    if not info['installed'] and not info['modifications']:
        print('Local Modifications : N/A (fresh adoption)')
    elif info['modifications']:
        print(f'Local Modifications : YES ({len(info["modifications"])} manual adjustment(s) detected)')
        for item in info['modifications']:
            print(f'  - {item}')
    else:
        print('Local Modifications : No (clean pinned bundle)')
    print('=============================================================================')


def sync_adoption(root, explicit_source=None, explicit_revision=None, force=False):
    info = inspect_status(root, explicit_source, explicit_revision)
    print_status_banner(info)
    if info['modifications'] and not force:
        raise PermissionError(
            'manual modifications detected in local standards; inspect diffs and confirm with the user before re-running with --force'
        )

    entries = info['entries']
    revision = info['global_revision']
    global_version = info['global_version']

    bundle_dir = root / '.nyssa-ai' / 'dev-standards' / LANGUAGE
    bundle_dir.mkdir(parents=True, exist_ok=True)
    for existing in authored_files(bundle_dir):
        rel = existing.relative_to(bundle_dir).as_posix()
        if rel not in entries:
            existing.unlink()
    for arcname, content in sorted(entries.items()):
        dest = bundle_dir / arcname
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(content)

    normalize_instruction_casing(root)
    fragment_lines = entries['adoption/AGENTS.fragment.md'].decode('utf-8').splitlines()
    start_idx = next((i for i, line in enumerate(fragment_lines) if line.startswith('# Development standards')), 0)
    fragment_section = '\n'.join(fragment_lines[start_idx:]).strip() + '\n'
    agents_path = root / 'AGENTS.md'
    agents_existed = agents_path.is_file()
    old_agents_text = agents_path.read_text(encoding='utf-8') if agents_existed else None
    migrated_from_host = None
    if old_agents_text is None or not old_agents_text.strip():
        for host_file in HOST_DELEGATE_FILES:
            candidate = root / host_file
            if candidate.is_file() and not candidate.is_symlink():
                candidate_body = candidate.read_text(encoding='utf-8').strip()
                if candidate_body and candidate_body not in {'@./AGENTS.md', '@AGENTS.md'}:
                    old_agents_text = candidate_body + '\n'
                    migrated_from_host = host_file
                    break

    heading_pattern = re.compile(rf'(?ms)^# Development standards \({re.escape(LANGUAGE)}\)[ \t]*\r?\n.*?(?=^# |\Z)')
    if old_agents_text is None or not old_agents_text.strip():
        new_agents_text = fragment_section
    elif heading_pattern.search(old_agents_text):
        new_agents_text = heading_pattern.sub(fragment_section.rstrip() + '\n\n', old_agents_text).rstrip() + '\n'
    else:
        new_agents_text = old_agents_text.rstrip() + '\n\n' + fragment_section
    agents_path.write_text(new_agents_text, encoding='utf-8')
    synced_instructions = ['AGENTS.md']

    for host_file in HOST_DELEGATE_FILES:
        candidate = root / host_file
        if candidate.is_symlink():
            synced_instructions.append(f'{host_file} (symlink)')
        elif candidate.is_file():
            body = candidate.read_text(encoding='utf-8').strip()
            if body in {'@./AGENTS.md', '@AGENTS.md'}:
                synced_instructions.append(f'{host_file} (@./AGENTS.md)')
            elif migrated_from_host == host_file:
                candidate.write_text('@./AGENTS.md\n', encoding='utf-8')
                synced_instructions.append(f'{host_file} (@./AGENTS.md)')
            elif old_agents_text is not None and body == old_agents_text.strip():
                candidate.write_text(new_agents_text, encoding='utf-8')
                synced_instructions.append(f'{host_file} (mirror)')
            elif force:
                candidate.write_text('@./AGENTS.md\n', encoding='utf-8')
                synced_instructions.append(f'{host_file} (@./AGENTS.md)')
        elif host_file == 'CLAUDE.md':
            candidate.write_text('@./AGENTS.md\n', encoding='utf-8')
            synced_instructions.append('CLAUDE.md (@./AGENTS.md)')

    for adapter_file in IDE_ADAPTER_FILES:
        candidate = root / adapter_file
        if candidate.is_file() and not candidate.is_symlink():
            body = candidate.read_text(encoding='utf-8')
            if body.strip() in {'@./AGENTS.md', '@AGENTS.md'}:
                synced_instructions.append(f'{adapter_file} (@./AGENTS.md)')
            elif old_agents_text is not None and body.strip() == old_agents_text.strip():
                candidate.write_text(new_agents_text, encoding='utf-8')
                synced_instructions.append(f'{adapter_file} (mirror)')
            elif heading_pattern.search(body):
                updated_body = heading_pattern.sub(fragment_section.rstrip() + '\n\n', body).rstrip() + '\n'
                candidate.write_text(updated_body, encoding='utf-8')
                synced_instructions.append(adapter_file)

    config_path = root / '.nyssa-ai' / 'dev-standards' / f'{LANGUAGE}.json'
    example_config = json.loads(entries['adoption/repository.example.json'].decode('utf-8'))
    if config_path.is_file():
        config = json.loads(config_path.read_text(encoding='utf-8'))
    else:
        config = example_config
    config['version'] = global_version
    config['revision'] = revision
    if not isinstance(config.get('source_roots'), list) or not config['source_roots']:
        config['source_roots'] = example_config['source_roots']
    if not isinstance(config.get('commands'), dict) or not ({'build', 'unit', 'use_case'} <= set(config['commands']) <= {'build', 'unit', 'use_case', 'e2e'}):
        config['commands'] = example_config['commands']
    config['file_hashes'] = {arcname: sha256_bytes(content) for arcname, content in sorted(entries.items())}
    config['agents_section_hash'] = sha256_bytes(fragment_section.encode('utf-8'))
    for src_rel in config['source_roots']:
        src_path = local_path(root, src_rel)
        if not src_path.exists() and src_rel == 'src':
            src_path.mkdir(parents=True, exist_ok=True)
    config_path.write_text(json.dumps(config, indent=2) + '\n', encoding='utf-8')

    gitignore = root / '.gitignore'
    if gitignore.is_file():
        lines = gitignore.read_text(encoding='utf-8').splitlines()
        cleaned = []
        for line in lines:
            stripped = line.strip()
            if stripped and not stripped.startswith('#') and not stripped.startswith('!'):
                normalized = stripped.lstrip('/').rstrip('/').removesuffix('/*').removesuffix('/**')
                if normalized in BANNED_GITIGNORE_TARGETS:
                    continue
            cleaned.append(line)
        non_comment = [l.strip() for l in cleaned if l.strip() and not l.strip().startswith('#')]
        if not any(l.lstrip('/').rstrip('/') == '.temp' for l in non_comment if not l.startswith('!')):
            if cleaned and cleaned[-1].strip():
                cleaned.append('')
            cleaned.extend([
                '# Mandatory repository-local scratch directory (REPO-SCRATCH)',
                '.temp/',
                '.worktrees/',
            ])
        gitignore.write_text('\n'.join(cleaned).rstrip() + '\n', encoding='utf-8')

    print(
        f'SYNCED: {LANGUAGE} standards bundle v{global_version} ({len(entries)} files, revision {revision}) '
        f'-> .nyssa-ai/dev-standards/{LANGUAGE} + [{", ".join(synced_instructions)}]'
    )
    return bundle_dir / 'scripts/check_adoption.py'


def local_path(root, value):
    if not isinstance(value, str) or not value or Path(value).is_absolute():
        raise ValueError('paths must be nonempty repository-relative strings')
    path = (root / value).resolve()
    if not path.is_relative_to(root):
        raise ValueError(f'path escapes repository: {value}')
    return path


def validate_gitignore(root):
    gitignore = root / '.gitignore'
    if not gitignore.is_file():
        return
    lines = [
        line.strip()
        for line in gitignore.read_text(encoding='utf-8').splitlines()
        if line.strip() and not line.strip().startswith('#')
    ]
    if not any(line.lstrip('/').rstrip('/') == '.temp' for line in lines if not line.startswith('!')):
        raise ValueError('.gitignore must ignore .temp/ (REPO-SCRATCH / REPO-GITIGNORE)')
    for line in lines:
        if line.startswith('!'):
            continue
        normalized = line.lstrip('/').rstrip('/').removesuffix('/*').removesuffix('/**')
        if normalized in BANNED_GITIGNORE_TARGETS:
            raise ValueError(f'.gitignore must not ignore tracked plugin or documentation state: {line} (REPO-GITIGNORE)')


def validate_repo_organization(root, sources):
    if (root / '.dev-standards').exists():
        raise ValueError('legacy .dev-standards directory found; migrate to .nyssa-ai/dev-standards')
    for child in root.iterdir():
        if child.is_dir() and child.name.lower() in BANNED_ROOT_DIRS:
            raise ValueError(f'prohibited top-level directory: {child.name} (see REPO-ROOT / REPO-SCRATCH)')
    agents_text = (root / 'AGENTS.md').read_text(encoding='utf-8')
    for host_file in ('CLAUDE.md', 'GEMINI.md', '.hermes.md', 'HERMES.md', 'AGENTS.override.md'):
        candidate = root / host_file
        if candidate.exists() and not candidate.is_symlink():
            body = candidate.read_text(encoding='utf-8').strip()
            if body not in {agents_text.strip(), '@./AGENTS.md', '@AGENTS.md'}:
                raise ValueError(f'{host_file} must symlink or delegate to AGENTS.md without diverging (REPO-GOVERNANCE)')
    validate_gitignore(root)
    test_root = (root / 'test').resolve()
    for source in sources:
        if source.is_relative_to(test_root):
            continue
        for file in source.rglob('*.cs'):
            if TEST_FILE_PATTERN.match(file.name):
                rel = file.relative_to(root).as_posix()
                raise ValueError(f'co-mingled test file in production source root: {rel} (isolate in test/unit or test/use_case)')


def validate(root):
    expected = root / '.nyssa-ai' / 'dev-standards' / LANGUAGE
    if PACKAGE != expected.resolve():
        raise ValueError(f'run the copied script at {expected / "scripts/check_adoption.py"} (or pass --sync to adopt/update the local bundle)')
    required = ['rules/repo-standards.md', 'rules/standards.md', 'references/testing.md', 'references/logging.md',
                'references/code-smells.md', 'examples/README.md', 'scripts/verify.py']
    checker = 'scripts/Checker/Checker.csproj'
    for relative in [*required, checker]:
        file = PACKAGE / relative
        if not file.is_file() or not file.resolve().is_relative_to(expected):
            raise ValueError(f'missing or external bundle file: {relative}')
    config = json.loads((root / '.nyssa-ai' / 'dev-standards' / f'{LANGUAGE}.json').read_text(encoding='utf-8'))
    if not isinstance(config, dict) or not re.fullmatch(r'[0-9a-fA-F]{40}', str(config.get('revision', ''))):
        raise ValueError('revision must name the exact 40-character source commit')
    instructions = (root / 'AGENTS.md').read_text(encoding='utf-8')
    for reference in (f'.nyssa-ai/dev-standards/{LANGUAGE}/rules/repo-standards.md',
                      f'.nyssa-ai/dev-standards/{LANGUAGE}/rules/standards.md'):
        if reference not in instructions:
            raise ValueError(f'AGENTS.md must reference {reference}')
    roots = config.get('source_roots')
    if not isinstance(roots, list) or not roots:
        raise ValueError('source_roots must be a nonempty array')
    sources = [local_path(root, value) for value in roots]
    for source in sources:
        if not source.is_dir():
            raise ValueError(f'missing source directory: {source}')
    validate_repo_organization(root, sources)
    commands = config.get('commands')
    if not isinstance(commands, dict) or not ({'build', 'unit', 'use_case'} <= set(commands) <= {'build', 'unit', 'use_case', 'e2e'}):
        raise ValueError('commands must define build, unit and use_case (and optional e2e)')
    prepared = {}
    for name, command in commands.items():
        if not isinstance(command, dict) or set(command) != {'cwd', 'argv'}:
            raise ValueError(f'{name} needs cwd and argv')
        cwd = local_path(root, command['cwd'])
        argv = command['argv']
        if not cwd.is_dir() or not isinstance(argv, list) or not argv or not all(isinstance(a, str) and a for a in argv):
            raise ValueError(f'{name} needs an existing cwd and a nonempty argument array')
        executable = shutil.which(argv[0])
        if executable is None:
            candidate = (cwd / argv[0]).resolve()
            if not candidate.is_file():
                raise ValueError(f'{name} executable unavailable: {argv[0]}')
            executable = str(candidate)
        prepared[name] = (cwd, [executable, *argv[1:]])
    runtime = 'dotnet'
    if shutil.which(runtime) is None:
        raise ValueError(f'checker runtime unavailable: {runtime}')
    return sources, prepared


def run(argv, cwd):
    print('+ ' + json.dumps(argv) + ' (cwd=' + str(cwd) + ')', flush=True)
    subprocess.run(argv, cwd=cwd, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('repository', type=Path)
    parser.add_argument('--status', action='store_true', help='inspect global vs. local version and check for manual adjustments without modifying files')
    parser.add_argument('--sync', action='store_true', help='populate or update .nyssa-ai/dev-standards/csharp and AGENTS.md from the plugin')
    parser.add_argument('--force', action='store_true', help='overwrite manually adjusted local standards files when running --sync')
    parser.add_argument('--source', type=Path, default=None, help='optional path to dev-standards plugin root when using --status or --sync')
    parser.add_argument('--revision', type=str, default=None, help='optional 40-character hex revision override when using --status or --sync')
    parser.add_argument('--run', action='store_true', help='run configured build/unit commands and source checks')
    parser.add_argument('--use-case', action='store_true', help='also run the configured use-case suite (requires --run)')
    parser.add_argument('--e2e', action='store_true', help='also run the configured e2e suite when present (requires --run)')
    args = parser.parse_args()
    if (args.use_case or args.e2e) and not args.run:
        parser.error('--use-case and --e2e require --run')
    if (args.source is not None or args.revision is not None) and not (args.sync or args.status):
        parser.error('--source and --revision require --status or --sync')
    if args.force and not args.sync:
        parser.error('--force requires --sync')
    root = args.repository.resolve()
    try:
        if args.status:
            info = inspect_status(root, args.source, args.revision)
            print_status_banner(info)
            return 0
        if args.sync:
            copied_script = sync_adoption(root, args.source, args.revision, args.force)
            if copied_script.resolve() != Path(__file__).resolve():
                forward = [sys.executable, str(copied_script), str(root)]
                if args.run:
                    forward.append('--run')
                if args.use_case:
                    forward.append('--use-case')
                if args.e2e:
                    forward.append('--e2e')
                return subprocess.run(forward).returncode
        sources, commands = validate(root)
        print(f'PASS: {LANGUAGE} local adoption paths, pin format, repo layout, instructions and command executables')
        if not args.run:
            print('Builds/tests were not executed. The recorded revision is not a byte-authenticity check.')
            return 0
        for name in ['build', 'unit', *(['use_case'] if args.use_case else []), *(['e2e'] if args.e2e and 'e2e' in commands else [])]:
            cwd, argv = commands[name]
            run(argv, cwd)
        for source in sources:
            argv = ['dotnet', 'run', '--project', str(PACKAGE / 'scripts/Checker/Checker.csproj'), '--', str(source)]
            run(argv, root)
        return 0
    except PermissionError as error:
        print(f'BLOCKED: {error}', file=sys.stderr)
        return 3
    except subprocess.CalledProcessError as error:
        print(f'FAIL: local command exited {error.returncode}; remaining commands were not run', file=sys.stderr)
        return 1
    except (OSError, ValueError, TypeError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
