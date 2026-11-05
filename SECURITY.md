# Security Policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability or include credentials, message bodies, customer namespace names, or other private data in a report. Use GitHub's private security advisory feature after the repository is published. Until that channel exists, contact the repository owner privately.

Include a minimal reproduction with synthetic data, affected version or commit, impact, and any suggested mitigation. Maintainers will acknowledge a report as soon as practical and coordinate disclosure after a fix is available.

## Scope and secret handling

On macOS, BusDock Studio stores connection strings as generic-password items in the user's Keychain. It never writes connection strings, SAS keys, tokens, or message bodies to the settings file or logs. The settings file contains only profile IDs, display names, namespace hostnames, and the appearance preference. Metadata may be displayed on screen because inspection is the product's purpose; users are responsible for protecting screenshots and copied content.

Never use production credentials in bug reports or automated tests. Rotate any credential that may have been exposed.
