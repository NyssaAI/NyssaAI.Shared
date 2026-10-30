"""Run locked tooling, sample behavior and public-header checker controls."""
from pathlib import Path
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = Path(__file__).resolve().parent

def run(args, cwd=PACKAGE):
    print('+ ' + ' '.join(map(str,args)), flush=True)
    return subprocess.run(list(map(str,args)),cwd=cwd,check=True)

if __name__ == '__main__':
    npm='npm.cmd' if os.name=='nt' else 'npm'
    run([npm,'ci','--ignore-scripts'])
    run([npm,'run','build'])
    run([npm,'run','check'])
    run([sys.executable, '-m', 'unittest', 'test/unit/typescript/test_checker.py'], cwd=ROOT)
    run([sys.executable, '-m', 'unittest', 'test/unit/typescript/test_adoption.py'], cwd=ROOT)
    run([npm,'test'])
    run([npm,'run','demo'])
