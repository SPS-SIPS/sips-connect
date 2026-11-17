#!/usr/bin/env python3
"""Reject JWT artifacts and private network addresses in tracked deployment samples.

Print paths and line numbers only; never echo detected values.
"""
import ipaddress
from pathlib import Path
import re
import subprocess
import sys

JWT = re.compile(r'(?<![\w-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![\w-])')
IPV4 = re.compile(r'(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])')
PRIVATE_NETWORKS = [ipaddress.ip_network(value) for value in ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16')]
SAMPLE_SUFFIXES = {'.json', '.http', '.yml', '.yaml', '.env', '.example'}

def violations(text, inspect_addresses):
    for number, line in enumerate(text.splitlines(), 1):
        if JWT.search(line):
            yield number, 'embedded JWT'
        if inspect_addresses:
            for match in IPV4.finditer(line):
                try:
                    address = ipaddress.ip_address(match.group())
                except ValueError:
                    continue
                if any(address in network for network in PRIVATE_NETWORKS):
                    yield number, 'private network address'
                    break

def main():
    paths = subprocess.check_output(['git', 'ls-files', '-z']).decode().split('\0')
    failures = 0
    for name in filter(None, paths):
        path = Path(name)
        try:
            text = path.read_text(encoding='utf-8')
        except (UnicodeError, OSError):
            continue
        for number, reason in violations(text, path.suffix.lower() in SAMPLE_SUFFIXES):
            print(f'{name}:{number}: {reason}')
            failures += 1
    print(f'Repository hygiene: {failures} violation(s).')
    return bool(failures)

if __name__ == '__main__':
    sys.exit(main())
