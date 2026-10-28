#!/usr/bin/env python3
import fcntl
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import sys
import tarfile
import tempfile


ROOT = Path.home() / "mist-book" / "site"
LIMIT = 100 * 1024 * 1024


def publish(revision, source, root=ROOT):
    if not re.fullmatch(r"[a-f0-9]{40}", revision):
        raise ValueError("Expected one full commit SHA")
    releases = root / "releases"
    releases.mkdir(parents=True, exist_ok=True)
    with (root / "deploy.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        with tempfile.TemporaryDirectory(dir=releases) as temporary:
            staging = Path(temporary)
            archive_path = staging / "upload.tar.gz"
            with archive_path.open("wb") as archive:
                remaining = LIMIT
                while block := source.read(min(remaining + 1, 1024 * 1024)):
                    remaining -= len(block)
                    if remaining < 0:
                        raise ValueError("Book archive exceeds 100 MiB")
                    archive.write(block)
            content = staging / "content"
            content.mkdir()
            with tarfile.open(archive_path, "r|gz") as archive:
                expanded_size = 0
                for index, member in enumerate(archive):
                    expanded_size += member.size
                    if expanded_size > LIMIT or index >= 10000:
                        raise ValueError("Expanded book exceeds 100 MiB or 10000 entries")
                    path = PurePosixPath(member.name)
                    if path.is_absolute() or ".." in path.parts:
                        raise ValueError("Archive path escapes release")
                    if not (member.isfile() or member.isdir()):
                        raise ValueError("Archive contains a link or special file")
                    if member.isdir():
                        (content / path).mkdir(parents=True, exist_ok=True)
                    else:
                        destination = content / path
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        with archive.extractfile(member) as incoming, destination.open("wb") as outgoing:
                            shutil.copyfileobj(incoming, outgoing)
                        destination.chmod(0o644)
            if not (content / "index.html").is_file():
                raise ValueError("Book has no index.html")
            (content / "revision.txt").write_text(revision + "\n")
            destination = releases / revision
            if not destination.exists():
                content.rename(destination)
            link = root / "next"
            link.unlink(missing_ok=True)
            link.symlink_to(Path("releases") / revision)
            link.replace(root / "current")
            previous = sorted(
                (path for path in releases.iterdir()
                 if path.is_dir() and re.fullmatch(r"[a-f0-9]{40}", path.name) and path.name != revision),
                key=lambda path: path.stat().st_mtime,
                reverse=True,
            )
            for obsolete in previous[9:]:
                shutil.rmtree(obsolete)
            print(f"Published {revision}")


if __name__ == "__main__":
    publish(os.environ.get("SSH_ORIGINAL_COMMAND", ""), sys.stdin.buffer)
