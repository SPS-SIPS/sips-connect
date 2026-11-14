#!/usr/bin/env python3
"""Compare an old credential with the review inventory without echoing it."""
import getpass
import hashlib
import sys

known = {
    '0c2d26e57ccd': 'R1: Core account zirat password',
    '05c710dfce54': 'R2: Core account repo password',
    'a8fce00af22b': 'R3: core-bank callback API key A',
    '901b20701af1': 'R3: core-bank callback API secret A',
    '371d0d25edd6': 'R4: core-bank callback API key B',
    'b93eb3602fe7': 'R4: core-bank callback API secret B',
    'a665a4592042': 'R7/R8: public test API credential (incoming Test entry / local core-bank defaults)',
    'a942b37ccfaf': 'R9: public default PostgreSQL password in local environments',
}
if not sys.stdin.isatty():
    sys.exit('Run interactively in a terminal; credentials are not accepted through arguments or pipes.')
value = getpass.getpass('Credential to identify (hidden; processed locally): ')
fingerprint = hashlib.sha256(value.encode('utf-8')).hexdigest()[:12]
print(known.get(fingerprint, 'No match in this inventory. This is not proof that the credential is safe.'))
