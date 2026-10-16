"""Docker Hub tag rules shared by publication and credential-free CI tests."""
import datetime
import os
import re
import subprocess
import sys


AUTOMATIC_BRANCHES = {'main', 'master', 'develop'}


def selected_branch(event, ref):
    if event not in {'push', 'workflow_dispatch'} or not ref.startswith('refs/heads/'):
        raise ValueError('Image publication requires a push or manual run on a branch.')
    branch = ref[len('refs/heads/'):]
    if not branch or (event == 'push' and branch not in AUTOMATIC_BRANCHES):
        raise ValueError('This branch can publish only through workflow_dispatch.')
    return branch


def image_tags(branch, source_sha, build_date):
    if not re.fullmatch(r'[0-9a-f]{40}', source_sha):
        raise ValueError('Expected a full source commit SHA.')
    if branch in {'main', 'master'}:
        alias = 'latest'
    elif branch == 'develop':
        alias = 'develop'
    else:
        # Existing project convention: tag the final component of the branch.
        alias = re.sub(r'[^a-z0-9_.-]+', '-', branch.rsplit('/', 1)[-1].lower()).lstrip('.-')[:128] or 'branch'
        if alias in {'latest', 'develop'} or re.fullmatch(r'\d{8}-[0-9a-f]{7}', alias):
            alias = 'branch-' + alias  # Feature aliases cannot replace stable/version tags.
    return alias, build_date.strftime('%Y%m%d') + '-' + source_sha[:7]


def main():
    branch = selected_branch(os.environ.get('GITHUB_EVENT_NAME', ''), os.environ.get('GITHUB_REF', ''))
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    alias, version = image_tags(branch, sha, datetime.datetime.now(datetime.timezone.utc))
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f'branch_tag={alias}\nversion_tag={version}\nsource_sha={sha}\n')
    print(f'Branch tag: {alias}; version tag: {version}; source commit: {sha}')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, subprocess.CalledProcessError) as error:
        print('Image tag generation failed: ' + str(error), file=sys.stderr)
        sys.exit(1)
