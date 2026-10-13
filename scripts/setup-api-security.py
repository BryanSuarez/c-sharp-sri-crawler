#!/usr/bin/env python3
"""Generate an administrator credential without exposing it to stdout or Git."""
import argparse
import base64
import hashlib
import os
from pathlib import Path
import secrets
import tempfile
import uuid


def write_private(path, value):
    descriptor, temporary = tempfile.mkstemp(prefix=".security-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w") as output:
            output.write(value)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("required", "localDevelopment"), default="required")
    parser.add_argument("--rotate", action="store_true", help="Replace existing administrator configuration and credential")
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    root = args.directory.resolve()
    config = root / ".env.security"
    secret_directory = root / ".secrets"
    credential = secret_directory / "administrator-api-key"
    if not args.rotate and (config.exists() or credential.exists()):
        parser.error("Private security files already exist. Use --rotate only for deliberate administrator rotation.")
    if config.is_symlink() or secret_directory.is_symlink() or credential.is_symlink():
        parser.error("Private security paths must not be symbolic links.")
    secret_directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(secret_directory, 0o700)
    identifier = uuid.uuid4().hex
    secret = base64.urlsafe_b64encode(secrets.token_bytes(32)).decode().rstrip("=")
    digest = base64.b64encode(hashlib.sha256(secret.encode("ascii")).digest()).decode()
    write_private(credential, f"sri_{identifier}.{secret}\n")
    write_private(config, f"ApiSecurity__Mode={args.mode}\nApiSecurity__AdministratorId={identifier}\n"
                  f"ApiSecurity__AdministratorHash={digest}\nApiSecurity__DefaultKeyLifetimeDays=90\n")
    print(f"Private configuration written to {config}")
    print(f"Administrator credential written to {credential}; restart the API after applying the migration.")


if __name__ == "__main__":
    main()
