"""Two real, disposable local SenseNet repositories and the current containerized tool.

Only Python's standard library and Docker Compose are needed. All generated local
configuration, credentials, logs and copied assemblies stay under ignored .private/.
No existing repository, database or Docker project is changed.
"""
import argparse
import http.server
import json
import os
import pathlib
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parents[2]
PRIVATE = ROOT / '.private' / 'multi-repository-lab'
PROJECT = 'index-tools-multirepo-lab'
SQL_IMAGE = 'mcr.microsoft.com/mssql/server@sha256:0ec7739e1c5ec2f57861facbe1f2b74f1d3e147c7c97edf91eeea920c5944d9c'
REPOSITORY_IMAGE = 'sensenetcsp/sn-api-sql@sha256:567ced0d27970d93cc34b27d744631669654c19595c4c0f58267b8b7a3fc54f2'
SDK_IMAGE = 'mcr.microsoft.com/dotnet/sdk:8.0.425-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80'
TOOL_IMAGE = 'index-tools:multirepo-local'
COMPOSE = ['docker', 'compose', '-p', PROJECT, '-f', str(PRIVATE / 'compose.json')]


def run(command, name=None):
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if name or result.returncode:
        (PRIVATE / ((name or 'last-command-error') + '.log')).write_text(redact(result.stdout + result.stderr), encoding='utf-8')
    if result.returncode:
        raise RuntimeError((name or 'Docker command') + ' failed; private logs contain redacted details.')
    return result.stdout.strip()


def redact(value):
    private_values = []
    if (PRIVATE / 'credentials.json').exists():
        private_values.extend(json.loads((PRIVATE / 'credentials.json').read_text()).values())
    for letter in 'ab':
        file = PRIVATE / 'tools' / ('api-key-' + letter)
        if file.exists(): private_values.append(file.read_text().strip())
    for secret in sorted(filter(None, private_values), key=len, reverse=True):
        value = value.replace(secret, '[redacted]')
    return value


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def prepare():
    PRIVATE.mkdir(parents=True, exist_ok=True)
    if os.name == 'nt':
        run(['icacls', str(PRIVATE), '/inheritance:r', '/grant:r',
             os.environ['USERDOMAIN'] + '\\' + os.environ['USERNAME'] + ':(OI)(CI)F'])
    else:
        (ROOT / '.private').chmod(0o700)
    for directory in ('tools', 'sql', 'published'):
        (PRIVATE / directory).mkdir(exist_ok=True)
    credentials_file = PRIVATE / 'credentials.json'
    if not credentials_file.exists():
        write_json(credentials_file, {name: 'Lab!' + secrets.token_hex(24) for name in
            ('sa', 'app-a', 'app-b', 'reader-a', 'reader-b', 'web-token')})
    credentials = json.loads(credentials_file.read_text())
    ports_file = PRIVATE / 'ports.json'
    if not ports_file.exists():
        write_json(ports_file, {'tools': 5192, 'repo-a': 5193, 'repo-b': 5194})
    ports = json.loads(ports_file.read_text())
    if len(set(ports.values())) != 3 or any(not 1024 <= p <= 65535 for p in ports.values()):
        raise ValueError('Three distinct local ports between 1024 and 65535 are required.')
    connection = lambda letter, role: (f'Server=sql;Database=IndexToolsRepo{letter.upper()};'
        f'User ID=lab_{role}_{letter};Password={credentials[role + "-" + letter]};TrustServerCertificate=True;')
    (PRIVATE / 'sql/sql-admin-password').write_text(credentials['sa'], encoding='utf-8')
    (PRIVATE / 'tools/web-token').write_text(credentials['web-token'], encoding='utf-8')
    statements = []
    for letter in 'ab':
        database = 'IndexToolsRepo' + letter.upper()
        statements.append(f"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];\nGO")
        for role in ('app', 'reader'):
            login = f'lab_{role}_{letter}'
            statements.append(f"IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD=N'{credentials[role + '-' + letter]}';\nGO")
        statements.append(f'USE [{database}];')
        for role in ('app', 'reader'):
            login = f'lab_{role}_{letter}'
            db_role = 'db_owner' if role == 'app' else 'db_datareader'
            statements.append(f"IF USER_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];\nALTER ROLE {db_role} ADD MEMBER [{login}];")
        statements.append('USE [master];\nGO')
        write_json(PRIVATE / ('repo-' + letter + '.json'), {
            'ConnectionStrings': {'SnCrMsSql': connection(letter, 'app')},
            'Serilog': {'MinimumLevel': {'Default': 'Warning'}},
            'sensenet': {'authentication': {'repositoryUrl': f'http://127.0.0.1:{ports["repo-" + letter]}', 'AddJwtCookie': False},
                        'AsposePreviewProvider': {'SkipLicenseCheck': True}}})
        (PRIVATE / 'tools' / ('sql-' + letter)).write_text(connection(letter, 'reader'), encoding='utf-8')
    (PRIVATE / 'sql/initialize.sql').write_text('\n'.join(statements), encoding='utf-8')
    runtime = {
        'SourcePath': '/source/repo-a/LocalIndex', 'RepositoryUrl': 'http://repo-a',
        'RepositoryBackupRoot': '/app/App_Data/IndexBackups', 'BackupSourceRoot': '/source/repo-a/IndexBackups',
        'SnapshotDirectory': '/state/copies', 'BackupDirectory': '/state/backups',
        'OutputDirectory': '/state/reports', 'DataDirectory': '/state/data', 'KeyDirectory': '/state/keys',
        'SqlConnectionStringFile': '/run/secrets/sql-a', 'ApiKeyFile': '/run/secrets/api-key-a'}
    write_json(PRIVATE / 'tools/runtime.json', runtime)
    for letter in 'ab':
        write_json(PRIVATE / 'tools' / ('runtime-' + letter + '.json'), dict(runtime,
            SourcePath='/source/repo-' + letter + '/LocalIndex', RepositoryUrl='http://repo-' + letter,
            BackupSourceRoot='/source/repo-' + letter + '/IndexBackups',
            SqlConnectionStringFile='/run/secrets/sql-' + letter, ApiKeyFile='/run/secrets/api-key-' + letter))
    private = PRIVATE.as_posix()
    services = {
        'sql': {'image': SQL_IMAGE, 'environment': {'ACCEPT_EULA': 'Y', 'MSSQL_PID': 'Developer', 'MSSQL_MEMORY_LIMIT_MB': '2048'},
            'entrypoint': ['/bin/bash', '-ec'],
            'command': ['export MSSQL_SA_PASSWORD="$$(cat /run/secrets/sql-admin-password)"; exec /opt/mssql/bin/sqlservr'],
            'volumes': ['sql-data:/var/opt/mssql', private + '/sql:/run/secrets:ro'],
            'healthcheck': {'test': ['CMD-SHELL', 'SQLCMDPASSWORD="$$(cat /run/secrets/sql-admin-password)" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SELECT 1" >/dev/null'],
                            'interval': '5s', 'timeout': '5s', 'retries': 60, 'start_period': '20s'},
            'networks': ['lab']},
        'database-init': {'image': SQL_IMAGE, 'entrypoint': ['/bin/bash', '-ec'],
            # Master can accept connections while the persisted user DBs still recover.
            # Every setup statement is idempotent, so wait through that startup window.
            'command': ['export SQLCMDPASSWORD="$$(cat /run/secrets/sql-admin-password)"; for attempt in {1..60}; do /opt/mssql-tools18/bin/sqlcmd -S sql -U sa -C -b -i /run/secrets/initialize.sql && exit 0; sleep 2; done; exit 1'],
            'volumes': [private + '/sql:/run/secrets:ro'], 'networks': ['lab'],
            'depends_on': {'sql': {'condition': 'service_healthy'}}}}
    for letter in 'ab':
        name = 'repo-' + letter
        services[name] = {'image': REPOSITORY_IMAGE,
            'environment': {'ASPNETCORE_ENVIRONMENT': 'Production', 'ASPNETCORE_URLS': 'http://+:80'},
            'ports': [f'127.0.0.1:{ports[name]}:80'], 'networks': ['lab'],
            'volumes': [name + '-data:/app/App_Data', private + '/' + name + '.json:/app/appsettings.Production.json:ro'],
            'depends_on': {'database-init': {'condition': 'service_completed_successfully'}}}
    services['tools'] = {'image': TOOL_IMAGE, 'user': '1654:1654', 'read_only': True, 'cap_drop': ['ALL'],
        'security_opt': ['no-new-privileges:true'], 'tmpfs': ['/tmp:mode=1777'], 'networks': ['lab'],
        'environment': {'INDEXTOOLS_CONFIG_FILE': '/run/secrets/runtime.json', 'INDEXTOOLS_WEB_TOKEN_FILE': '/run/secrets/web-token',
                        'AllowRemoteAccess': 'true', 'WebAccess__HttpsRedirect': 'false', 'AllowedHosts': 'localhost;127.0.0.1'},
        'ports': [f'127.0.0.1:{ports["tools"]}:8080'],
        'volumes': ['repo-a-data:/source/repo-a:ro', 'repo-b-data:/source/repo-b:ro', 'tool-state:/state', private + '/tools:/run/secrets:ro']}
    write_json(PRIVATE / 'compose.json', {'name': PROJECT, 'services': services,
        'volumes': {name: {} for name in ('sql-data', 'repo-a-data', 'repo-b-data', 'tool-state')}, 'networks': {'lab': {}}})
    return ports


def sql_query(query):
    # The SQL text has only fixed lab identifiers; passwords are read inside SQL's container.
    return run(COMPOSE + ['exec', '-T', 'sql', '/bin/bash', '-ec',
        'export SQLCMDPASSWORD="$(cat /run/secrets/sql-admin-password)"; /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -Q "' + query + '"'])


def repository_request(base, path, key, payload=None):
    request = urllib.request.Request(base + path, data=None if payload is None else json.dumps(payload).encode(),
        headers={'apikey': key, 'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def wait_repositories(ports):
    profiles = []
    for letter in 'ab':
        base = f'http://127.0.0.1:{ports["repo-" + letter]}'
        database = 'IndexToolsRepo' + letter.upper()
        deadline = time.monotonic() + 420
        while time.monotonic() < deadline:
            try:
                key = sql_query(f"SET NOCOUNT ON; SELECT TOP 1 Value FROM {database}.dbo.AccessTokens WHERE UserId=1 AND Feature='apikey' AND ExpirationDate>GETUTCDATE() ORDER BY ExpirationDate DESC;")
                if key and '\n' not in key and ' ' not in key:
                    repository_request(base, "/odata.svc/('Root')/QueryIndexBackup", key)
                    break
            except Exception:
                pass
            time.sleep(2)
        else:
            raise RuntimeError('Repo ' + letter.upper() + ' did not become ready (schema, API authentication and native-backup query required).')
        (PRIVATE / 'tools' / ('api-key-' + letter)).write_text(key, encoding='utf-8')
        for parent, name in [("/odata.svc/Root('Content')", 'IndexToolsLab'),
                             ("/odata.svc/Root/Content('IndexToolsLab')", 'RepairTarget'),
                             ("/odata.svc/Root/Content('IndexToolsLab')", 'Repo' + letter.upper())]:
            lookup = parent.replace("('", '/').replace("')", '') + "('" + name + "')?$select=Id"
            try:
                repository_request(base, lookup, key)
            except urllib.error.HTTPError as error:
                if error.code != 404: raise
                try:
                    repository_request(base, parent, key, {'__ContentType': 'Folder', 'Name': name})
                except urllib.error.HTTPError as create_error:
                    # A concurrent install/index commit can make the initial lookup stale.
                    # Accept an existing node only after a successful authenticated readback.
                    if create_error.code != 403: raise
                    repository_request(base, lookup, key)
        profiles.append({'Name': 'Repo ' + letter.upper(), 'Description': 'Isolated local Docker test repository ' + letter.upper(),
            'IndexPath': '/source/repo-' + letter + '/LocalIndex',
            'ConnectionString': (PRIVATE / 'tools' / ('sql-' + letter)).read_text(),
            'RepositoryUrl': 'http://repo-' + letter, 'ApiKey': key, 'RepositoryPath': '/Root/Content/IndexToolsLab',
            'RepositoryBackupRoot': '/app/App_Data/IndexBackups', 'BackupSourceRoot': '/source/repo-' + letter + '/IndexBackups',
            'DefaultOutputPath': '/state/reports', 'IsFavorite': True, 'Tags': ['local-multirepo-lab']})
        print('REPO_' + letter.upper() + '_READY ' + base, flush=True)
    write_json(PRIVATE / 'tools/profiles.json', profiles)


def seed_profiles():
    # Copy the current published assemblies, then use ConfigurationService itself.
    # The helper runs without a network and shares only the lab's private state/keys.
    container = run(['docker', 'create', '--network', 'none', TOOL_IMAGE])
    try:
        run(['docker', 'cp', container + ':/app/web/.', str(PRIVATE / 'published')])
    finally:
        run(['docker', 'rm', container])
    run(['docker', 'run', '--rm', '--network', 'none', '--user', '1654:1654',
        '--mount', 'type=bind,source=' + str(PRIVATE / 'published') + ',target=/app/web,readonly',
        '--mount', 'type=bind,source=' + str(ROOT / 'docker/local/setup') + ',target=/setup,readonly',
        '--mount', 'type=bind,source=' + str(PRIVATE / 'tools') + ',target=/run/secrets,readonly',
        '--mount', 'type=volume,source=' + PROJECT + '_tool-state,target=/state',
        '-e', 'HOME=/tmp', '-e', 'DOTNET_CLI_HOME=/tmp', '-e', 'DOTNET_NOLOGO=1',
        '-e', 'INDEXTOOLS_CONFIG_FILE=/run/secrets/runtime.json', SDK_IMAGE,
        '/bin/sh', '-ec', 'dotnet build /setup/LabSetup.csproj -c Release --nologo '
        '-p:BaseIntermediateOutputPath=/tmp/lab-obj/ -p:BaseOutputPath=/tmp/lab-bin/ '
        '-p:RestoreSources=/tmp && dotnet /tmp/lab-bin/Release/net8.0/LabSetup.dll'], 'profile-setup')
    print('REPO_A_AND_B_PROFILES_SAVED_ENCRYPTED', flush=True)


def open_browser(base, handoff_timeout=45):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args): return None
    token = (PRIVATE / 'tools/web-token').read_text().strip()
    try:
        urllib.request.build_opener(NoRedirect).open(base + '/login', urllib.parse.urlencode({'token': token}).encode(), timeout=15)
        raise RuntimeError('Expected a local login redirect')
    except urllib.error.HTTPError as response:
        if response.code != 302: raise RuntimeError('Local web login failed') from None
        cookies = response.headers.get_all('Set-Cookie', [])
    if not any(c.startswith('IndexToolsSession=') for c in cookies):
        raise RuntimeError('Local session cookie was not issued')
    class Handoff(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_): pass
        def do_GET(self):
            if self.path != '/':
                self.send_response(404); self.end_headers(); return
            self.send_response(302)
            for cookie in cookies: self.send_header('Set-Cookie', cookie)
            self.send_header('Location', base + '/operations')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Content-Length', '0')
            self.end_headers(); self.server.completed = True
    with http.server.HTTPServer(('127.0.0.1', 0), Handoff) as server:
        server.timeout = 1; server.completed = False
        webbrowser.open('http://127.0.0.1:' + str(server.server_port) + '/')
        deadline = time.monotonic() + handoff_timeout
        while not server.completed and time.monotonic() < deadline: server.handle_request()
        if not server.completed: raise RuntimeError('Browser handoff timed out')


def start(no_build=False, no_browser=False):
    ports = prepare()
    print('STARTING_TWO_REPOSITORY_DOCKER_LAB', flush=True)
    run(COMPOSE + ['up', '-d', 'repo-a', 'repo-b'], 'repositories-start')
    wait_repositories(ports)
    if not no_build:
        print('BUILDING_CURRENT_INDEX_TOOLS', flush=True)
        run(['docker', 'build', '-t', TOOL_IMAGE, '.'], 'tools-build')
    # The state volume gets the image's UID1654-owned directories on first creation.
    # Keep the web process stopped while the separate setup process writes profiles.
    run(COMPOSE + ['stop', 'tools'], 'tools-stop-for-profile-setup')
    run(COMPOSE + ['create', 'tools'], 'tools-create')
    seed_profiles()
    run(COMPOSE + ['up', '-d', '--no-deps', '--wait', '--wait-timeout', '120', 'tools'], 'tools-start')
    base = f'http://127.0.0.1:{ports["tools"]}'
    with urllib.request.urlopen(base + '/healthz', timeout=10) as response:
        if response.read() != b'healthy': raise RuntimeError('Tool health check failed')
    if not no_browser: open_browser(base)
    print('TWO_REPOSITORY_LAB_READY ' + base + '/operations', flush=True)


def check():
    """Portable native acceptance against only the two explicitly provisioned lab DBs."""
    image = 'index-tools:multirepo-tests'
    print('BUILDING_CURRENT_LAB_TESTS', flush=True)
    run(['docker', 'build', '--target', 'tests', '-t', image, '.'], 'tests-build')
    assembly = '/src/test/IndexTools.Tests/bin/Release/net8.0/IndexTools.Tests.dll'
    run(['docker', 'run', '--rm', '--network', 'none', image, 'dotnet', 'vstest', assembly,
         '--TestCaseFilter:FullyQualifiedName!~LocalWorkflowIntegrationTests'], 'regular-regressions')
    print('REGULAR_REGRESSIONS_PASS', flush=True)
    summary = {}
    for letter in 'ab':
        run(['docker', 'run', '--rm', '--user', '1654:1654', '--network', PROJECT + '_lab',
            '--mount', 'type=bind,source=' + str(PRIVATE / 'tools') + ',target=/run/secrets,readonly',
            '--mount', 'type=volume,source=' + PROJECT + '_repo-a-data,target=/source/repo-a,readonly',
            '--mount', 'type=volume,source=' + PROJECT + '_repo-b-data,target=/source/repo-b,readonly',
            '--mount', 'type=volume,source=' + PROJECT + '_tool-state,target=/state',
            '-e', 'HOME=/tmp', '-e', 'DOTNET_CLI_HOME=/tmp', '-e', 'INDEXTOOLS_CONTAINER_RUNTIME=1',
            '-e', 'INDEXTOOLS_LOCAL_ACCEPTANCE=1', '-e', 'INDEXTOOLS_CONFIG_FILE=/run/secrets/runtime-' + letter + '.json',
            image, 'dotnet', 'vstest', assembly, '--TestCaseFilter:FullyQualifiedName~LocalWorkflowIntegrationTests'], 'native-repo-' + letter)
        evidence = json.loads(run(COMPOSE + ['exec', '-T', 'tools', 'cat', '/state/reports/local-acceptance-repo-' + letter + '.json']))
        if not evidence.get('Success'): raise RuntimeError('Native acceptance evidence did not confirm success.')
        summary['repo-' + letter] = evidence
        print('NATIVE_REPO_' + letter.upper() + '_PASS', flush=True)
    write_json(PRIVATE / 'acceptance-summary.json', summary)
    print('TWO_REPOSITORY_NATIVE_ACCEPTANCE_PASS', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--no-build', action='store_true', help='Reuse the previously built tool image.')
    parser.add_argument('--no-browser', action='store_true', help='Start without opening an authenticated browser.')
    parser.add_argument('--stop', action='store_true', help='Stop only this lab; preserve volumes and credentials.')
    parser.add_argument('--check', action='store_true', help='Build and run regressions plus native integration for both repos.')
    args = parser.parse_args()
    try:
        if args.stop:
            if not (PRIVATE / 'compose.json').exists(): raise RuntimeError('This lab has not been created.')
            run(COMPOSE + ['stop'], 'lab-stop')
            print('TWO_REPOSITORY_LAB_STOPPED_DATA_PRESERVED')
        else:
            start(args.no_build, args.no_browser)
            if args.check: check()
    except Exception as error:
        # HTTP/subprocess errors may contain keys or command input; never dump them.
        print('TWO_REPOSITORY_LAB_FAILED ' + type(error).__name__ + ': ' +
              (str(error) if isinstance(error, (RuntimeError, ValueError)) else 'See private redacted logs.'), file=sys.stderr)
        sys.exit(1)
