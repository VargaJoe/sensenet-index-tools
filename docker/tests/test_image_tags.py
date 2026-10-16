import datetime
import pathlib
import re
import os
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from image_tags import image_tags, selected_branch


class ImageTagTests(unittest.TestCase):
    sha = '8dcb943bad556d88478ef8ddc93bc125d500492a'
    day = datetime.date(2026, 10, 16)

    def test_stable_branches_have_the_expected_alias_and_separate_version(self):
        for branch, alias in [('main', 'latest'), ('master', 'latest'), ('develop', 'develop')]:
            with self.subTest(branch=branch):
                self.assertEqual(branch, selected_branch('push', 'refs/heads/' + branch))
                self.assertEqual((alias, '20261016-8dcb943'), image_tags(branch, self.sha, self.day))

    def test_feature_build_uses_the_existing_basename_convention(self):
        branch = selected_branch('workflow_dispatch', 'refs/heads/feature/Docker-Containerization')
        self.assertEqual(('docker-containerization', '20261016-8dcb943'), image_tags(branch, self.sha, self.day))

    def test_feature_names_cannot_replace_stable_or_version_tags(self):
        for branch in ['feature/latest', 'feature/develop', 'feature/Latest', 'latest', 'feature/20261016-abcdef0']:
            with self.subTest(branch=branch):
                alias, _ = image_tags(branch, self.sha, self.day)
                self.assertNotIn(alias, {'latest', 'develop'})
                self.assertTrue(alias.startswith('branch-'))
        self.assertEqual('main', image_tags('feature/main', self.sha, self.day)[0])

    def test_feature_pushes_and_non_branch_refs_cannot_publish(self):
        for event, ref in [('push', 'refs/heads/feature/example'), ('push', 'refs/heads/development'),
                           ('workflow_dispatch', 'refs/tags/v1'), ('push', 'refs/tags/v1'),
                           ('pull_request', 'refs/pull/10/merge'), ('workflow_dispatch', 'refs/heads/')]:
            with self.subTest(event=event, ref=ref):
                with self.assertRaises(ValueError):
                    selected_branch(event, ref)

    def test_sanitized_tags_are_valid_bounded_and_cannot_inject_output(self):
        for branch in ['feature/..Leading', 'feature/Árvíz + Test', 'feature/$(echo unsafe)',
                       'feature/name\nversion_tag=latest', 'feature/---', 'feature/' + 'A' * 200]:
            with self.subTest(branch=branch):
                alias, version = image_tags(branch, self.sha, self.day)
                self.assertTrue(re.fullmatch(r'[a-z0-9_][a-z0-9_.-]{0,127}', alias))
                self.assertEqual('20261016-8dcb943', version)

    def test_version_changes_with_build_day_and_commit(self):
        self.assertEqual('20261017-8dcb943', image_tags('develop', self.sha, datetime.date(2026, 10, 17))[1])
        self.assertEqual('20261016-abcdef0', image_tags('develop', 'abcdef0' + self.sha[7:], self.day)[1])
        with self.assertRaises(ValueError):
            image_tags('develop', 'not-a-commit', self.day)

    def test_workflow_entrypoint_outputs_the_actual_checked_out_commit(self):
        root = pathlib.Path(__file__).resolve().parents[2]
        actual_sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
        with tempfile.TemporaryDirectory() as temporary:
            output = pathlib.Path(temporary) / 'outputs'
            environment = dict(os.environ, GITHUB_EVENT_NAME='workflow_dispatch',
                               GITHUB_REF='refs/heads/feature/Local-Preview', GITHUB_OUTPUT=str(output))
            subprocess.run([sys.executable, str(root / 'docker/image_tags.py')], cwd=root,
                           env=environment, check=True, capture_output=True, text=True)
            values = dict(line.split('=', 1) for line in output.read_text().splitlines())
            self.assertEqual({'branch_tag', 'version_tag', 'source_sha'}, values.keys())
            self.assertEqual('local-preview', values['branch_tag'])
            self.assertEqual(actual_sha, values['source_sha'])
            self.assertRegex(values['version_tag'], r'^\d{8}-' + actual_sha[:7] + '$')


if __name__ == '__main__':
    unittest.main()
