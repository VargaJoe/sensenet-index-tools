"""Create an isolated local repository. Never print credentials or container logs."""
import json
import os
import pathlib
import shutil
import secrets
import subprocess
import sys
import time
import urllib.request
import urllib.error

ROOT = pathlib.Path(__file__).resolve().parents[2]
PRIVATE = ROOT / '.private' / 'local'
TOOLS = PRIVATE / 'tools'
SQL = PRIVATE / 'sql'
COMPOSE = ['docker', 'compose', '-p', 'index-tools-lab', '--env-file', str(PRIVATE / 'compose.env'), '-f', str(ROOT / 'docker/local/compose.yaml')]

def lab_port(name, default):
    values = {}
    if (PRIVATE / 'compose.env').exists():
        values = dict(line.split('=', 1) for line in (PRIVATE / 'compose.env').read_text().splitlines() if '=' in line and not line.lstrip().startswith('#'))
    port = int(os.environ.get(name, values.get(name, default)))
    if not 1024 <= port <= 65535: raise ValueError('Local lab port must be between 1024 and 65535')
    return port

def repository_base(): return 'http://127.0.0.1:' + str(lab_port('LAB_REPOSITORY_PORT', 5186))
def tools_base(): return 'http://127.0.0.1:' + str(lab_port('LAB_TOOLS_PORT', 5189))

def run(arguments, **kwargs):
    return subprocess.run(arguments, check=True, capture_output=True, text=True, encoding='utf-8', errors='replace', **kwargs)

def prepare():
    PRIVATE.mkdir(parents=True, exist_ok=True)
    if os.name != 'nt': (ROOT / '.private').chmod(0o700)
    TOOLS.mkdir(exist_ok=True)
    SQL.mkdir(exist_ok=True)
    # Preserve credentials from an earlier local setup; never reset an existing database.
    for filename, destination in [('sql-admin-password', SQL), ('initialize.sql', SQL), ('runtime.json', TOOLS),
                                  ('sql-connection', TOOLS), ('api-key', TOOLS), ('web-token', TOOLS)]:
        if (PRIVATE / filename).exists() and not (destination / filename).exists():
            shutil.copyfile(PRIVATE / filename, destination / filename)
    if (TOOLS / 'runtime.json').exists():
        return
    passwords = {key: 'Lab!' + secrets.token_hex(24) for key in ('sql-admin-password', 'repository-password', 'reader-password', 'web-token')}
    (SQL / 'sql-admin-password').write_text(passwords['sql-admin-password'], encoding='utf-8')
    (TOOLS / 'web-token').write_text(passwords['web-token'], encoding='utf-8')
    connection = lambda user, password: f'Server=sql;Database=IndexToolsLab;User ID={user};Password={password};TrustServerCertificate=True;'
    (TOOLS / 'sql-connection').write_text(connection('index_reader', passwords['reader-password']), encoding='utf-8')
    repository = {
        'ConnectionStrings': {'SnCrMsSql': connection('repository_app', passwords['repository-password'])},
        'Serilog': {'MinimumLevel': {'Default': 'Warning'}},
        'sensenet': {'authentication': {'repositoryUrl': repository_base(), 'AddJwtCookie': False},
                     'AsposePreviewProvider': {'SkipLicenseCheck': True}}
    }
    (PRIVATE / 'repository.json').write_text(json.dumps(repository, indent=2), encoding='utf-8')
    sql = f"""IF DB_ID(N'IndexToolsLab') IS NULL CREATE DATABASE [IndexToolsLab];
GO
IF SUSER_ID(N'repository_app') IS NULL CREATE LOGIN [repository_app] WITH PASSWORD=N'{passwords['repository-password']}';
IF SUSER_ID(N'index_reader') IS NULL CREATE LOGIN [index_reader] WITH PASSWORD=N'{passwords['reader-password']}';
GO
USE [IndexToolsLab];
IF USER_ID(N'repository_app') IS NULL CREATE USER [repository_app] FOR LOGIN [repository_app];
ALTER ROLE db_owner ADD MEMBER [repository_app];
IF USER_ID(N'index_reader') IS NULL CREATE USER [index_reader] FOR LOGIN [index_reader];
ALTER ROLE db_datareader ADD MEMBER [index_reader];
"""
    (SQL / 'initialize.sql').write_text(sql, encoding='utf-8')
    settings = {'SourcePath': '/source/repository/LocalIndex', 'RepositoryUrl': 'http://repository',
                'RepositoryBackupRoot': '/app/App_Data/IndexBackups', 'BackupSourceRoot': '/source/repository/IndexBackups',
                'SnapshotDirectory': '/state/copies', 'BackupDirectory': '/state/backups',
                'OutputDirectory': '/state/reports', 'DataDirectory': '/state/data', 'KeyDirectory': '/state/keys',
                'SqlConnectionStringFile': '/run/secrets/sql-connection', 'ApiKeyFile': '/run/secrets/api-key'}
    (TOOLS / 'runtime.json').write_text(json.dumps(settings, indent=2), encoding='utf-8')
    (PRIVATE / 'compose.env').write_text('LAB_PRIVATE_DIRECTORY=' + PRIVATE.as_posix() + '\nLAB_REPOSITORY_PORT=' + str(lab_port('LAB_REPOSITORY_PORT', 5186)) + '\nLAB_TOOLS_PORT=' + str(lab_port('LAB_TOOLS_PORT', 5189)) + '\n', encoding='utf-8')
    (TOOLS / 'api-key').write_text('', encoding='utf-8')
    if os.name == 'nt':
        run(['icacls', str(PRIVATE), '/inheritance:r', '/grant:r', os.environ['USERDOMAIN'] + '\\' + os.environ['USERNAME'] + ':(OI)(CI)F'])
    else:
        # Parent is private on the host; bind-mounted children must be readable by container UIDs.
        (ROOT / '.private').chmod(0o700)

def provision():
    prepare()
    run(COMPOSE + ['up', '-d', 'repository'])
    for attempt in range(150):
        try:
            with urllib.request.urlopen(repository_base() + '/', timeout=3) as response:
                if response.status == 200:
                    break
        except Exception:
            pass
        time.sleep(2)
    else:
        raise RuntimeError('Local repository did not become ready. Inspect private logs with credential redaction.')
    # Obtain the key generated by SenseNet for the administrator of this new, isolated DB.
    query = "SET NOCOUNT ON; SELECT TOP 1 Value FROM IndexToolsLab.dbo.AccessTokens WHERE UserId=1 AND Feature='apikey' AND ExpirationDate>GETUTCDATE() ORDER BY ExpirationDate DESC;"
    for attempt in range(120):
        try:
            key = run(COMPOSE + ['exec', '-T', 'sql', '/bin/bash', '-ec',
                'export SQLCMDPASSWORD="$(cat /run/secrets/sql-admin-password)"; /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -Q "' + query + '"']).stdout.strip()
            if key and '\n' not in key and ' ' not in key:
                break
        except subprocess.CalledProcessError:
            pass  # HTTP can listen while SenseNet is still installing its schema.
        time.sleep(2)
    else:
        raise RuntimeError('Could not obtain the local repository API credential.')
    (TOOLS / 'api-key').write_text(key, encoding='utf-8')
    # Generic test content; only this disposable lab is addressed.
    for parent, name in [("/odata.svc/Root('Content')", 'IndexToolsLab'),
                         ("/odata.svc/Root/Content('IndexToolsLab')", 'RepairTarget'),
                         ("/odata.svc/Root/Content('IndexToolsLab')", 'KeepTarget')]:
        path = '/Root/Content/IndexToolsLab' + ('' if name == 'IndexToolsLab' else '/' + name)
        parts = path.split('/')
        lookup = '/odata.svc' + '/'.join(parts[:-1]) + "('" + parts[-1] + "')?$select=Id"
        try:
            with urllib.request.urlopen(urllib.request.Request(repository_base() + lookup, headers={'apikey': key}), timeout=15) as response:
                response.read()
        except urllib.error.HTTPError as error:
            if error.code != 404: raise RuntimeError('Local demo content lookup failed') from None
            payload = json.dumps({'__ContentType': 'Folder', 'Name': name}).encode()
            with urllib.request.urlopen(urllib.request.Request(repository_base() + parent, data=payload,
                    headers={'apikey': key, 'Content-Type': 'application/json'}), timeout=30) as response:
                response.read()
    print('LOCAL_REPOSITORY_READY ' + repository_base() + '/ (private API credential stored)', flush=True)

if __name__ == '__main__':
    try:
        provision()
    except Exception as error:
        # CalledProcessError contains command input/output; do not expose it.
        print('LOCAL_SETUP_FAILED: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
