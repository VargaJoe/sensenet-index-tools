"""Repeatable acceptance against the explicitly created local SQL/SenseNet lab."""
import argparse
import json
import pathlib
import subprocess
import sys
import time
import urllib.request
from initialize import ROOT, PRIVATE, TOOLS, SQL, COMPOSE, provision, tools_base

def redact(text):
    values = []
    for file in [TOOLS / 'api-key', TOOLS / 'web-token', TOOLS / 'sql-connection', SQL / 'sql-admin-password']:
        if file.exists(): values.append(file.read_text().strip())
    for connection in list(values):
        for part in connection.split(';'):
            if '=' in part and part.split('=', 1)[0].strip().lower() in ('password', 'pwd'):
                values.append(part.split('=', 1)[1])
    for value in sorted((v for v in values if v), key=len, reverse=True): text = text.replace(value, '[redacted]')
    return text

def execute(name, command):
    print(name.upper() + '_STARTED', flush=True)
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    output = redact(result.stdout + result.stderr)
    (PRIVATE / (name + '.log')).write_text(output, encoding='utf-8')
    if result.returncode:
        print('\n'.join(output.splitlines()[-35:]))
        raise RuntimeError(name + ' failed; see its private log')
    print(name.upper() + '_PASS', flush=True)

def check(no_build=False, browser=False):
    provision()
    if not no_build:
        execute('test-image-build', ['docker', 'build', '--target', 'tests', '-t', 'index-tools:operator-tests', '.'])
        execute('runtime-image-build', ['docker', 'build', '-t', 'index-tools:operator-local', '.'])
    execute('unit-regressions', ['docker', 'run', '--rm', 'index-tools:operator-tests', 'dotnet', 'vstest',
        '/src/test/IndexTools.Tests/bin/Release/net8.0/IndexTools.Tests.dll', '--TestCaseFilter:FullyQualifiedName!~LocalWorkflowIntegrationTests'])
    execute('local-web-start', COMPOSE + ['--profile', 'tools', 'up', '-d', '--no-deps', '--wait', 'tools'])
    command = ['docker', 'run', '--rm', '--user', '1654:1654', '--network', 'index-tools-lab_lab',
        '--mount', 'type=bind,source=' + str(TOOLS) + ',target=/run/secrets,readonly',
        '--mount', 'type=volume,source=index-tools-lab_repository-data,target=/source/repository,readonly',
        '--mount', 'type=volume,source=index-tools-lab_tool-state,target=/state',
        '-e', 'HOME=/tmp', '-e', 'DOTNET_CLI_HOME=/tmp', '-e', 'INDEXTOOLS_CONFIG_FILE=/run/secrets/runtime.json',
        '-e', 'INDEXTOOLS_CONTAINER_RUNTIME=1', '-e', 'INDEXTOOLS_LOCAL_ACCEPTANCE=1']
    if browser: command += ['-e', 'INDEXTOOLS_PREPARE_BROWSER_FIXTURE=1']
    command += ['index-tools:operator-tests', 'dotnet', 'vstest', '/src/test/IndexTools.Tests/bin/Release/net8.0/IndexTools.Tests.dll',
                '--TestCaseFilter:FullyQualifiedName~LocalWorkflowIntegrationTests']
    execute('native-integration', command)
    cli = COMPOSE + ['exec', '-T', 'tools', 'dotnet', '/app/cli/sn-index-maintenance-suite.dll', 'workspace', 'copy', '--source', 'live', '--offline']
    active = subprocess.run(cli, cwd=ROOT, capture_output=True, text=True)
    if active.returncode == 0 or 'writer' not in (active.stdout + active.stderr).lower():
        raise RuntimeError('Offline copy did not positively reject the running repository writer')
    print('ACTIVE_WRITER_COPY_REJECTED_PASS', flush=True)
    # Host-side test orchestration only. The application has no Docker administration access.
    try:
        execute('local-repository-stop-for-offline-test', COMPOSE + ['stop', 'repository'])
        execute('offline-copy', cli)
    finally:
        execute('local-repository-restart', COMPOSE + ['up', '-d', '--no-deps', 'repository'])
    key = (TOOLS / 'api-key').read_text().strip()
    for attempt in range(60):
        try:
            from initialize import repository_base
            with urllib.request.urlopen(urllib.request.Request(repository_base() + "/odata.svc/('Root')/QueryIndexBackup", headers={'apikey': key}), timeout=3) as response:
                if response.status == 200: break
        except Exception: pass
        time.sleep(1)
    else: raise RuntimeError('Local repository did not resume after the offline-copy test')
    print('REPOSITORY_RESUMED_PASS', flush=True)
    container_id = subprocess.check_output(COMPOSE + ['ps', '-q', 'tools'], text=True).strip()
    container = json.loads(subprocess.check_output(['docker', 'inspect', container_id]))[0]
    assert container['Config']['User'] == '1654:1654'
    assert container['HostConfig']['ReadonlyRootfs'] and not container['HostConfig']['Privileged']
    assert next(m for m in container['Mounts'] if m['Destination'] == '/source/repository')['RW'] is False
    assert all(m['Destination'] not in ('/var/run/docker.sock', '/run/docker.sock') for m in container['Mounts'])
    summary = subprocess.check_output(COMPOSE + ['exec', '-T', 'tools', 'cat', '/state/reports/local-acceptance.json'], text=True)
    (PRIVATE / 'acceptance-summary.json').write_text(summary, encoding='utf-8')
    if browser: execute('browser-acceptance', ['node', 'docker/local/browser-check.cjs', '--repairs'])
    print('LOCAL_ACCEPTANCE_COMPLETE ' + tools_base() + '/operations', flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--no-build', action='store_true')
    parser.add_argument('--browser', action='store_true')
    args = parser.parse_args()
    try: check(args.no_build, args.browser)
    except Exception as error:
        print('LOCAL_ACCEPTANCE_FAILED: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
