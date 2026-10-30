"""Verify this independent package against its pinned canonical dependency and checker controls."""
from pathlib import Path
import json
import os
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = Path(__file__).resolve().parent
EXAMPLES = ROOT / 'skills/dev-standards/references/csharp/examples'

def run(args, cwd=PACKAGE, **kwargs):
    print('+ ' + ' '.join(map(str, args)), flush=True)
    return subprocess.run(list(map(str, args)), cwd=cwd, check=True, **kwargs)

def main():
    lock = json.loads((PACKAGE / 'dependency-lock.json').read_text())
    checkout = os.environ.get('NYSSA_SHARED_ROOT')
    if not checkout:
        sys.exit('Set NYSSA_SHARED_ROOT to an authorized Nyssa.Shared checkout. See README.md; no dependency is silently substituted.')
    run(['git', '-C', checkout, 'cat-file', '-e', lock['revision'] + '^{commit}'])
    snapshot = PACKAGE / '.temp' / 'nyssa' / lock['revision']
    project = snapshot / lock['project']
    if not project.exists():
        snapshot.mkdir(parents=True, exist_ok=True)
        archive = PACKAGE / '.temp' / 'nyssa.tar'
        with archive.open('wb') as output:
            run(['git','-C',checkout,'archive',lock['revision'],'--','Nyssa.Result','Directory.Build.props','Directory.Build.targets'], stdout=output)
        with tarfile.open(archive) as contents:
            contents.extractall(snapshot, filter='data')
        archive.unlink()
    # The supplied checkout is never built or changed; build outputs stay inside this package.
    property_argument = '-p:NyssaResultProject=' + str(project)
    run(['dotnet','build',PACKAGE/'Checker/Checker.csproj','--nologo'])
    run(['dotnet','run','--no-build','--project',PACKAGE/'Checker/Checker.csproj','--',EXAMPLES])
    run([sys.executable, '-m', 'unittest', 'test/unit/csharp/test_checker.py'], cwd=ROOT)
    run([sys.executable, '-m', 'unittest', 'test/unit/csharp/test_adoption.py'], cwd=ROOT)
    run([sys.executable, PACKAGE/'verify_persistence.py'])
    run(['dotnet','run','--project',ROOT/'test/use_case/csharp/Tests/Tests.csproj',property_argument])
    run(['dotnet','run','--project',EXAMPLES/'Demo/Demo.csproj',property_argument])

if __name__ == '__main__':
    main()
