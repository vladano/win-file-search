# Security policy

## Supported versions

Only the latest release receives fixes. Please update before reporting.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability** ([direct link](https://github.com/vladano/win-file-search/security/advisories/new)). Include:

- what the problem is and what an attacker could do with it,
- steps or a file that reproduces it,
- the app version and Windows version.

You should get a reply within 7 days. Once a fix is released, the advisory is published and you are credited unless you prefer not to be.

## Scope

File Search runs locally with the current user's rights and makes no network connections. Relevant problems include, for example:

- a crafted file (text, Office or PDF) that crashes the app, hangs it, or exhausts memory,
- unsafe handling of the command line or the Explorer context-menu arguments,
- the registry entries written by **Add to Explorer context menu** (`HKCU` only).

Release downloads come with a `.sha256` checksum. The exe is not code-signed yet.
