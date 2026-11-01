#!/usr/bin/env python3
"""Regenerates Test/Mist.Tests/Common/Diagnostics/TextFreezeTable.cs from the registry sources.

The table pins the rendered text of every descriptor character for character (diagnostics spec
Requirement 7.9). Run it only as part of a deliberate, reviewed wording change; the Tier 1 test
TextFreezeTests fails on any drift until the table is regenerated on purpose.

Positional placeholders {n} render as "argn". A named slot at schema position i renders as "argi"
for Name, Type, Symbol, Path, Enum, Text, as the single element of a Sequence and as the carried
value of an Optional. Quantity and Boolean use render-only argi sentinels to preserve the
frozen text independently of schema migration. A descriptor with Optional slots
gets one extra row per Optional slot with that slot absent, keyed "Field|absent:slot".

An Optional Type slot directly followed by another Optional slot is an alternative pair: every
reporting site fills exactly one of the two. The pair shares one schema position, so later slots
render one index lower, and the descriptor gets a single row with no absent rows; both choices
must render that row, as TextFreezeTests.Variants checks.

Sources are matched case-sensitively and read in POSIX path order, so every platform writes the
same table.
"""
import fnmatch
import glob
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def sources(pattern):
    found = (os.path.relpath(path, ROOT).replace(os.sep, '/') for path in glob.glob(os.path.join(ROOT, pattern)))
    return [path for path in found if fnmatch.fnmatchcase(path, pattern)]


REGISTRY = sorted(sources('Source/Mist/Common/Diagnostics/Descriptors/*.cs') +
                  sources('Source/Mist/Common/Diagnostics/*Diagnostics*.cs'))
OUTPUT = os.path.join(ROOT, 'Test/Mist.Tests/Common/Diagnostics/TextFreezeTable.cs')

FIELD = re.compile(
    r'public static readonly MistDiagnostic (\w+) = new\(\s*DiagnosticNumber\.Mint\("[^"]+"\),\s*'
    r'Title: "(?:[^"\\]|\\.)*",\s*Template: ((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+),(.*?)\);(?=\n|$)', re.S)
SLOT = re.compile(r'new Slot\("(\w+)", SlotKind\.(\w+)(?:, SlotKind\.(\w+))?\)')


def unescape(literal):
    out = []
    i = 0
    while i < len(literal):
        c = literal[i]
        if c == '\\':
            n = literal[i + 1]
            out.append({'n': '\n', 't': '\t', 'r': '\r', '"': '"', '\\': '\\', "'": "'", '0': '\0'}[n])
            i += 2
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def template_text(expr):
    parts = re.findall(r'"((?:[^"\\]|\\.)*)"', expr)
    return ''.join(unescape(p) for p in parts)


def render(template, value_of, present):
    out = []
    i = 0
    while i < len(template):
        c = template[i]
        if c == '{':
            if template[i + 1] == '{':
                out.append('{')
                i += 2
                continue
            close = min(x for x in (template.find('}', i), template.find('?', i)) if x >= 0)
            name = template[i + 1:close]
            if template[close] == '}':
                out.append(value_of(name))
                i = close + 1
                continue
            depth, end = 1, close + 1
            while depth > 0:
                if template.startswith('{{', end) or template.startswith('}}', end):
                    end += 2
                    continue
                depth += {'{': 1, '}': -1}.get(template[end], 0)
                end += 1
            end -= 1
            if present(name):
                out.append(render(template[close + 1:end], value_of, present))
            i = end + 1
            continue
        if c == '}':
            out.append('}')
            i += 2
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def rows():
    for path in REGISTRY:
        src = open(os.path.join(ROOT, path), encoding='utf-8').read()
        cls = re.search(r'class (\w+)', src).group(1)
        for m in FIELD.finditer(src):
            field, template, rest = m.group(1), template_text(m.group(2)), m.group(3)
            slots = SLOT.findall(rest)
            key = f'{cls}.{field}'
            if key in ('CommonDiagnostics.PackageNotFound', 'CommonDiagnostics.PackageNetworkError'):
                yield key, 'arg0'
                continue
            if key == 'DriverDiagnostics.ReferenceCycle':
                yield key, render(template, lambda n: 'arg0', lambda n: n == 'chain')
                continue
            if not slots:
                yield key, render(template, lambda n: f'arg{n}', lambda n: True)
                continue
            index, pairs = positions(slots)
            kinds = {name: (kind, element) for name, kind, element in slots}

            def value(name, absent=None):
                kind, element = kinds[name]
                if kind == 'Optional' and name == absent:
                    return ''
                return f'arg{index[name]}'

            if pairs:
                yield key, alternative(key, template, index, pairs)
                continue

            yield key, render(template, lambda n: value(n), lambda n: True)
            for name, kind, element in slots:
                if kind == 'Optional':
                    yield f'{key}|absent:{name}', render(template, lambda n, a=name: value(n, a), lambda n, a=name: n != a)


def positions(slots):
    index, pairs, position, i = {}, [], 0, 0
    while i < len(slots):
        name, kind, element = slots[i]
        index[name] = position
        if kind == 'Optional' and element == 'Type' and i + 1 < len(slots) and slots[i + 1][1] == 'Optional':
            index[slots[i + 1][0]] = position
            pairs.append((name, slots[i + 1][0]))
            i += 1
        i += 1
        position += 1
    return index, pairs


def alternative(key, template, index, pairs):
    texts = set()
    for choice in (0, 1):
        absent = {pair[1 - choice] for pair in pairs}
        texts.add(render(template, lambda n: '' if n in absent else f'arg{index[n]}', lambda n: n not in absent))
    if len(texts) != 1:
        raise SystemExit(f'{key}: alternative pairs render differently: {sorted(texts)}')
    return texts.pop()


def cs_literal(text):
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r').replace('\t', '\\t') + '"'


def main():
    lines = [
        '// Generated by tools/diagnostics/freeze_table.py; regenerate only in a reviewed wording change.',
        'namespace Mist.Tests.Common.Diagnostics;',
        '',
        'internal static class TextFreezeTable',
        '{',
        '    public static readonly IReadOnlyDictionary<string, string> Frozen = new Dictionary<string, string>(StringComparer.Ordinal)',
        '    {',
    ]
    count = 0
    for key, text in rows():
        lines.append(f'        [{cs_literal(key)}] = {cs_literal(text)},')
        count += 1
    lines += ['    };', '}']
    with open(OUTPUT, 'w', encoding='utf-8', newline='\n') as out:
        out.write('\n'.join(lines))
    print(f'{count} rows -> {os.path.relpath(OUTPUT, ROOT)}')


if __name__ == '__main__':
    sys.exit(main())
