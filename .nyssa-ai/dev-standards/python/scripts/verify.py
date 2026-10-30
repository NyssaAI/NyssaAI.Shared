"""Run observable Python examples, strict type checking, linting, and bounded checker controls."""
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src/python'
EXAMPLES = ROOT / 'skills/dev-standards/references/python/examples'


def run(args, cwd=ROOT):
    print('+ ' + ' '.join(map(str, args)), flush=True)
    return subprocess.run(list(map(str, args)), cwd=cwd, check=True)


if __name__ == '__main__':
    run([sys.executable, str(SRC / 'check.py'), str(EXAMPLES)])
    if shutil.which('ruff'):
        run(['ruff', 'check', str(EXAMPLES), str(SRC / 'check.py')])
    run([sys.executable, '-m', 'mypy', '--config-file', str(EXAMPLES / 'pyproject.toml'), str(EXAMPLES)])
    run([sys.executable, '-m', 'unittest', 'test.unit.python.test_checker'])
    run([sys.executable, '-m', 'unittest', 'test.unit.python.test_adoption'])
    run([sys.executable, '-m', 'pytest', 'test/use_case/python'])
    run([sys.executable, str(EXAMPLES / 'demo.py')])
