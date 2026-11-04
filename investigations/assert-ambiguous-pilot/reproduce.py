import hashlib
import json
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
HERE = pathlib.Path(__file__).resolve().parent
RAW = ROOT / 'TestResults/assert-ambiguous-pilot'
FILTER = 'FullyQualifiedName~Mist.Tests.Semantics.ScopeResolverAmbiguityTests'
SOURCE = ROOT / 'Source/Mist/ProgramModel/Model/Scopes/ImportingScope.cs'
TEST = ROOT / 'Test/Mist.Tests/Semantics/ScopeResolverAmbiguityTests.cs'
REPORT = HERE / 'checks.json'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def record(label, ledger, exit_code, restoration=None):
    print((ledger / 'summary.txt').read_text())
    print((ledger / 'failures.txt').read_text())
    tree = ET.parse(next((ledger / 'trx').glob('*.trx')))
    results = tree.findall('.//t:UnitTestResult', NS)
    inventory = []
    for case in tree.findall('.//t:UnitTest', NS):
        method = case.find('t:TestMethod', NS)
        inventory.append(method.attrib['className'] + '.' + method.attrib['name'])
    failed = sorted(r.attrib['testName'] for r in results if r.attrib['outcome'] == 'Failed')
    expected_failures = sorted([
        'TwoImportedNamespaces_SameTypeName_ReportsAmbiguity',
        'ThreeImports_TwoHaveSameName_ReportsAmbiguity',
        'TwoImportedNamespaces_SameArityBearingName_ReportsAmbiguity',
        'TwoProjectedImports_SameTypeName_StillReportsAmbiguity',
    ]) if label == 'candidate-mutation' else []
    observed = sorted(name.rsplit('.', 1)[-1] for name in failed)
    classifications = {name: tier or 'unclassified' for tier, name in re.findall(
        r'\[Fact\]\s*(?:\[Trait\("Tier", "(\d)"\)\]\s*)?public void (\w+)', TEST.read_text())}
    assert len(classifications) == 19
    build = (ledger / 'build.log').read_text()
    data = {
        'command': ['tools/test-ledger/run.sh', 'Test/Mist.Tests', '--filter', FILTER],
        'head': git('rev-parse', 'HEAD'),
        'storm': git('-C', 'Source/Storm.SourceGenerator', 'rev-parse', 'HEAD'),
        'ledger': str(ledger.relative_to(ROOT)), 'exitCode': exit_code,
        'total': len(results), 'passed': sum(r.attrib['outcome'] == 'Passed' for r in results),
        'failed': failed, 'inventory': sorted(inventory), 'testSha256': sha(TEST),
        'classification': classifications,
        'restoration': restoration,
        'rawSha256': {str(p.relative_to(ROOT)): sha(p) for p in sorted(ledger.rglob('*')) if p.is_file()},
    }
    checks = json.loads(REPORT.read_text()) if REPORT.exists() else {}
    checks[label] = data
    REPORT.write_text(json.dumps(checks, indent=2) + '\n')
    assert len(results) == 19 and observed == expected_failures, data
    assert data['passed'] == 19 - len(expected_failures), data
    assert exit_code == (1 if expected_failures else 0), data
    if expected_failures:
        assert (ledger / 'failures.txt').read_text().count('Got ErrorType with 0 diagnostics.') == 4
    assert re.search(r'\b0 Warning\(s\)', build), build
    assert not re.search(r': warning ', build), build
    if 'baseline' in checks:
        assert data['inventory'] == checks['baseline']['inventory']
    print(json.dumps({k: data[k] for k in ['ledger', 'total', 'passed', 'failed', 'restoration']}, indent=2))


def run(label):
    RAW.mkdir(parents=True, exist_ok=True)
    predicate = 'result is ErrorType || diags.Any(d => d.Number.Value == "MIST0299")'
    corrected = 'result is ErrorType && diags.Any(d => d.Number.Value == "MIST0299" && d.Severity == DiagnosticSeverity.Error)'
    assert (predicate if label.startswith('baseline') else corrected) in TEST.read_text()
    original = SOURCE.read_bytes()
    before = sha(SOURCE)
    restoration = None
    if label.endswith('-mutation'):
        report = b'diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AmbiguousType, node, null, new NameValue(request.Name), new NameValue(matchedNamespace!), new NameValue(conflictNamespace));'
        assert original.count(report) == 1
        SOURCE.write_bytes(original.replace(report, b'_ = (node, diagnostics);'))
        restoration = {'before': before, 'mutated': sha(SOURCE)}
        (RAW / (label + '.patch')).write_bytes(subprocess.check_output(['git', 'diff', '--', str(SOURCE.relative_to(ROOT))], cwd=ROOT))
    try:
        result = subprocess.run(['tools/test-ledger/run.sh', 'Test/Mist.Tests', '--filter', FILTER], cwd=ROOT)
        ledger = (ROOT / 'TestResults/ledger/latest').resolve()
    finally:
        if restoration is not None:
            SOURCE.write_bytes(original)
            restoration['after'] = sha(SOURCE)
            (RAW / (label + '-restoration.json')).write_text(json.dumps(restoration, indent=2) + '\n')
        assert sha(SOURCE) == before
    record(label, ledger, result.returncode, restoration)


def recovery(label):
    probe = RAW / 'recovery'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'Program.cs').write_bytes((HERE / 'Recovery.cs.txt').read_bytes())
    (probe / 'Recovery.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../../Test/Mist.Tests/Mist.Tests.csproj" /></ItemGroup>
</Project>''')
    command = ['dotnet', 'run', '--project', str(probe / 'Recovery.csproj')]
    output = RAW / (label + '-recovery.txt')
    with output.open('w') as stream:
        result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
    print(output.read_text())
    checks = json.loads(REPORT.read_text())
    checks[label + '-recovery'] = {'command': command, 'exitCode': result.returncode, 'raw': str(output.relative_to(ROOT)), 'sha256': sha(output)}
    REPORT.write_text(json.dumps(checks, indent=2) + '\n')
    assert result.returncode == 0 and output.read_text().count('PASS ') == 2
    assert ': warning ' not in output.read_text()


def scope():
    baseline = 'd3768f0b42459f8d840108d84d207679c3fd66ef'
    original = subprocess.check_output(['git', 'show', baseline + ':' + str(TEST.relative_to(ROOT))], cwd=ROOT)
    expected = original.replace(
        b'result is ErrorType || diags.Any(d => d.Number.Value == "MIST0299")',
        b'result is ErrorType && diags.Any(d => d.Number.Value == "MIST0299" && d.Severity == DiagnosticSeverity.Error)')
    callers = [
        'TwoImportedNamespaces_SameTypeName_ReportsAmbiguity',
        'ThreeImports_TwoHaveSameName_ReportsAmbiguity',
        'TwoImportedNamespaces_SameArityBearingName_ReportsAmbiguity',
        'TwoProjectedImports_SameTypeName_StillReportsAmbiguity',
    ]
    for name in callers:
        old = ('    [Fact]\n    public void ' + name).encode()
        new = ('    [Fact]\n    [Trait("Tier", "2")]\n    public void ' + name).encode()
        assert expected.count(old) == 1
        expected = expected.replace(old, new)
    assert TEST.read_bytes() == expected
    assert not git('diff', baseline, '--', 'Source', 'specs', 'samples')
    assert not git('-C', 'Source/Storm.SourceGenerator', 'status', '--porcelain')
    changed = git('diff', '--name-only', baseline).splitlines()
    assert all(p == str(TEST.relative_to(ROOT)) or p.startswith('investigations/assert-ambiguous-pilot/') for p in changed)
    checks = json.loads(REPORT.read_text())
    checks['scope'] = {
        'baseline': baseline, 'verifiedSourceCommit': git('rev-parse', 'HEAD'),
        'testSha256': sha(TEST), 'productionSha256': sha(SOURCE),
        'classificationDelta': {name: 'unclassified -> Tier 2' for name in callers},
        'otherTests': '15 remain unclassified; selection does not filter by Tier',
        'exactAllowedSourceDelta': True, 'productionSpecsSamplesUnchanged': True,
        'changedPaths': changed,
    }
    REPORT.write_text(json.dumps(checks, indent=2) + '\n')
    print(json.dumps(checks['scope'], indent=2))


def verify():
    for name, command in [
        ('build', ['dotnet', 'build', 'Mist.slnx']),
        ('format', ['dotnet', 'format', 'Mist.slnx', '--verify-no-changes']),
    ]:
        output = RAW / (name + '.log')
        with output.open('w') as stream:
            result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        print(output.read_text(), flush=True)
        checks = json.loads(REPORT.read_text())
        checks[name] = {'command': command, 'exitCode': result.returncode, 'raw': str(output.relative_to(ROOT)), 'sha256': sha(output)}
        REPORT.write_text(json.dumps(checks, indent=2) + '\n')
        assert result.returncode == 0
        assert ': warning ' not in output.read_text()
        if name == 'build':
            assert re.search(r'\b0 Warning\(s\)', output.read_text())
            assert re.search(r'\b0 Error\(s\)', output.read_text())


if __name__ == '__main__':
    if sys.argv[1] == 'capture':
        saved = json.loads(REPORT.read_text()) if REPORT.exists() else {}
        record(sys.argv[2], ROOT / sys.argv[3], int(sys.argv[4]), saved.get(sys.argv[2], {}).get('restoration'))
    elif sys.argv[1] == 'recovery':
        recovery(sys.argv[2])
    elif sys.argv[1] == 'scope':
        scope()
    elif sys.argv[1] == 'verify':
        verify()
    else:
        run(sys.argv[1])
