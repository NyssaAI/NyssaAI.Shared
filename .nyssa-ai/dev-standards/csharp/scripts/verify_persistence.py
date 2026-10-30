"""Run the bounded C# persistence example; Docker is only used by --use-case."""
from pathlib import Path
import argparse
import json
import os
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--use-case', action='store_true', help='Run real PostgreSQL tests instead of fast unit tests.')
    args = parser.parse_args()
    lock = json.loads((PACKAGE / 'dependency-lock.json').read_text())
    checkout = os.environ.get('NYSSA_SHARED_ROOT')
    if not checkout:
        sys.exit('Set NYSSA_SHARED_ROOT to an authorized Nyssa.Shared checkout; no Result substitute is used.')
    subprocess.run(['git', '-C', checkout, 'cat-file', '-e', lock['revision'] + '^{commit}'], check=True)
    snapshot = PACKAGE / '.temp' / 'nyssa' / lock['revision']
    project = snapshot / lock['project']
    if not project.exists():
        snapshot.mkdir(parents=True, exist_ok=True)
        archive = PACKAGE / '.temp' / 'nyssa-persistence.tar'
        try:
            with archive.open('wb') as output:
                subprocess.run(['git', '-C', checkout, 'archive', lock['revision'], '--',
                                'Nyssa.Result', 'Directory.Build.props', 'Directory.Build.targets'],
                               stdout=output, check=True)
            with tarfile.open(archive) as contents:
                contents.extractall(snapshot, filter='data')
        finally:
            archive.unlink(missing_ok=True)
    suite = 'UseCaseTests' if args.use_case else 'UnitTests'
    command = ['dotnet', 'run', '--project', str(ROOT / 'test/use_case/csharp/Persistence' / suite / (suite + '.csproj')),
               '-p:NyssaResultProject=' + str(project)]
    print('+ ' + ' '.join(command), flush=True)
    subprocess.run(command, cwd=PACKAGE, check=True)


if __name__ == '__main__':
    main()
