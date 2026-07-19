#!/usr/bin/env python3
"""Process encrypted Win7 upload jobs from the Ubuntu-backed SMB share."""

from __future__ import annotations

import argparse
import base64
import fcntl
import hashlib
import json
import logging
from logging.handlers import RotatingFileHandler
import os
from pathlib import Path, PurePosixPath
import shlex
import shutil
import socket
import sys
import tarfile
import time
from typing import Any
import unicodedata
import xml.etree.ElementTree as ET

import paramiko
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa


SCHEMA_VERSION = "1"
PUBLIC_KEY_NAME = "nas-upload-public.xml"
STATUS_FILE_NAME = ".ubuntu-win-sync-status.xml"


class PermanentJobError(Exception):
    pass


class TransientJobError(Exception):
    pass


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def safe_error(exc: BaseException, secret: str = "") -> str:
    message = str(exc).replace("\r", " ").replace("\n", " ").strip()
    if secret:
        message = message.replace(secret, "***")
    return message[:500] or exc.__class__.__name__


def int_bytes(value: int) -> bytes:
    width = max(1, (value.bit_length() + 7) // 8)
    return value.to_bytes(width, "big")


def public_key_xml(public_key: rsa.RSAPublicKey) -> str:
    numbers = public_key.public_numbers()
    modulus = base64.b64encode(int_bytes(numbers.n)).decode("ascii")
    exponent = base64.b64encode(int_bytes(numbers.e)).decode("ascii")
    return (
        "<RSAKeyValue><Modulus>"
        + modulus
        + "</Modulus><Exponent>"
        + exponent
        + "</Exponent></RSAKeyValue>"
    )


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def ensure_mode(path: Path, mode: int) -> None:
    try:
        path.chmod(mode)
    except OSError:
        pass


def atomic_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    os.replace(str(temporary), str(path))


def write_public_key(private_key: rsa.RSAPrivateKey, path: Path) -> None:
    atomic_text(path, public_key_xml(private_key.public_key()))
    ensure_mode(path, 0o644)


def load_or_create_encryption_key(state_root: Path) -> rsa.RSAPrivateKey:
    key_path = state_root / "credential-private-key.pem"
    if key_path.exists():
        key = serialization.load_pem_private_key(key_path.read_bytes(), password=None)
        if not isinstance(key, rsa.RSAPrivateKey):
            raise RuntimeError("Credential key is not RSA.")
        return key

    key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
    pem = key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    key_path.parent.mkdir(parents=True, exist_ok=True)
    key_path.write_bytes(pem)
    ensure_mode(key_path, 0o600)
    return key


def load_or_create_nas_key(state_root: Path) -> tuple[paramiko.RSAKey, Path]:
    key_path = state_root / "nas-client-rsa.pem"
    if key_path.exists():
        key = paramiko.RSAKey.from_private_key_file(str(key_path))
    else:
        key = paramiko.RSAKey.generate(bits=3072)
        key.write_private_key_file(str(key_path))
        ensure_mode(key_path, 0o600)
    atomic_text(state_root / "nas-client-rsa.pub", nas_public_line(key) + "\n")
    ensure_mode(state_root / "nas-client-rsa.pub", 0o644)
    return key, key_path


def nas_public_line(key: paramiko.RSAKey) -> str:
    return f"{key.get_name()} {key.get_base64()} ubuntu-win-share-agent"


def initialize_runtime(
    share_root: Path,
    state_root: Path,
    allowed_hosts: list[str],
    poll_seconds: int,
) -> dict[str, Any]:
    share_root = share_root.expanduser().resolve()
    state_root = state_root.expanduser().resolve()
    state_root.mkdir(parents=True, exist_ok=True)
    ensure_mode(state_root, 0o700)

    control_root = share_root / ".ubuntu-win-share"
    for path in (
        control_root / "keys",
        control_root / "jobs" / "incoming",
        control_root / "jobs" / "processing",
        control_root / "jobs" / "results",
        control_root / "jobs" / "failed",
    ):
        path.mkdir(parents=True, exist_ok=True)

    private_key = load_or_create_encryption_key(state_root)
    write_public_key(private_key, control_root / "keys" / PUBLIC_KEY_NAME)
    load_or_create_nas_key(state_root)

    known_hosts = state_root / "known_hosts"
    known_hosts.touch(exist_ok=True)
    ensure_mode(known_hosts, 0o600)

    config = {
        "version": 1,
        "share_root": str(share_root),
        "state_root": str(state_root),
        "allowed_hosts": sorted({host.strip() for host in allowed_hosts if host.strip()}),
        "poll_seconds": max(5, int(poll_seconds)),
    }
    atomic_text(state_root / "config.json", json.dumps(config, indent=2) + "\n")
    ensure_mode(state_root / "config.json", 0o600)
    return config


def load_config(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if int(data.get("version", 0)) != 1:
        raise RuntimeError("Unsupported agent config version.")
    return data


def load_ssh_lookup(host: str) -> dict[str, Any]:
    config_path = Path.home() / ".ssh" / "config"
    if not config_path.exists():
        return {}
    ssh_config = paramiko.SSHConfig()
    with config_path.open("r", encoding="utf-8", errors="replace") as handle:
        ssh_config.parse(handle)
    return ssh_config.lookup(host)


def host_fingerprint(key: paramiko.PKey) -> str:
    digest = hashlib.sha256(key.asbytes()).digest()
    return "SHA256:" + base64.b64encode(digest).decode("ascii").rstrip("=")


def parse_job(path: Path) -> dict[str, Any]:
    root = ET.parse(path).getroot()
    if root.tag != "NasUploadJob" or root.get("version") != SCHEMA_VERSION:
        raise PermanentJobError("Unsupported job schema.")

    def value(name: str, required: bool = True) -> str:
        node = root.find(name)
        text = "" if node is None or node.text is None else node.text.strip()
        if required and not text:
            raise PermanentJobError(f"Missing job field: {name}")
        return text

    try:
        port = int(value("NasPort"))
    except ValueError as exc:
        raise PermanentJobError("Invalid NAS port.") from exc
    if port < 1 or port > 65535:
        raise PermanentJobError("Invalid NAS port.")

    job = {
        "job_id": value("JobId"),
        "client_id": value("ClientId"),
        "created_utc": value("CreatedUtc"),
        "computer": value("Computer"),
        "profile": value("Profile"),
        "source_relative": value("SourceRelative"),
        "nas_host": value("NasHost"),
        "nas_port": port,
        "nas_username": value("NasUsername"),
        "nas_auth_mode": value("NasAuthMode"),
        "nas_remote_path": value("NasRemotePath"),
        "password_cipher": value("NasPasswordCipher", required=False),
        "cipher_algorithm": value("CipherAlgorithm"),
        "public_key_sha256": value("PublicKeySha256"),
        "delete_remote_files": value("DeleteRemoteFiles").lower() == "true",
    }
    if job["nas_auth_mode"] not in ("password", "ssh-key"):
        raise PermanentJobError("Unsupported NAS authentication mode.")
    if job["nas_auth_mode"] == "password" and not job["password_cipher"]:
        raise PermanentJobError("Encrypted NAS password is missing.")
    if job["delete_remote_files"]:
        raise PermanentJobError("Remote deletion is disabled by this agent.")
    return job


def validate_source(share_root: Path, relative: str) -> Path:
    pure = PurePosixPath(relative.replace("\\", "/"))
    if pure.is_absolute() or ".." in pure.parts or not pure.parts:
        raise PermanentJobError("Unsafe source path.")
    if pure.parts[0].lower() != "win7sync":
        raise PermanentJobError("Source must be under Win7Sync.")
    source = (share_root / Path(*pure.parts)).resolve()
    if os.path.commonpath([str(share_root.resolve()), str(source)]) != str(
        share_root.resolve()
    ):
        raise PermanentJobError("Source escapes the shared root.")
    if not source.is_dir():
        raise TransientJobError("Synced source directory is not available.")
    return source


def validate_remote_path(value: str) -> PurePosixPath:
    pure = PurePosixPath(value.replace("\\", "/"))
    if pure.is_absolute() or ".." in pure.parts or not pure.parts:
        raise PermanentJobError("NAS target must be a relative account-home path.")
    return pure


def safe_remote_segment(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value)
    output: list[str] = []
    changed = False
    previous_underscore = False
    for character in normalized:
        if character.isascii() and (
            character.isalnum() or character in "._-"
        ):
            output.append(character)
            previous_underscore = False
        else:
            changed = True
            if not previous_underscore:
                output.append("_")
                previous_underscore = True
    safe = "".join(output).strip("._-") or "item"
    if safe != normalized:
        changed = True
    if changed:
        digest = hashlib.sha256(value.encode("utf-8")).hexdigest()[:8]
        safe += "-" + digest
    return safe


def safe_remote_path(path: PurePosixPath) -> PurePosixPath:
    return PurePosixPath(*(safe_remote_segment(part) for part in path.parts))


def decrypt_password(
    job: dict[str, Any],
    private_key: rsa.RSAPrivateKey,
    public_key_path: Path,
) -> str:
    if job["nas_auth_mode"] != "password":
        return ""
    if job["cipher_algorithm"] != "RSA-OAEP-SHA1":
        raise PermanentJobError("Unsupported credential cipher.")
    if sha256_file(public_key_path).lower() != job["public_key_sha256"].lower():
        raise PermanentJobError("Upload public key fingerprint mismatch.")
    try:
        cipher = base64.b64decode(job["password_cipher"], validate=True)
        plain = private_key.decrypt(
            cipher,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA1()),
                algorithm=hashes.SHA1(),
                label=None,
            ),
        )
        return plain.decode("utf-8")
    except Exception as exc:
        raise PermanentJobError("Unable to decrypt NAS credentials.") from exc


def connect_client(
    job: dict[str, Any],
    state_root: Path,
    password: str,
    allowed_hosts: list[str],
) -> tuple[paramiko.SSHClient, str, bool, dict[str, Any]]:
    requested_host = job["nas_host"]
    lookup = load_ssh_lookup(requested_host)
    hostname = str(lookup.get("hostname", requested_host))
    port = int(lookup.get("port", job["nas_port"]))
    username = job["nas_username"] or str(lookup.get("user", ""))
    identity_files = lookup.get("identityfile", [])
    if isinstance(identity_files, str):
        identity_files = [identity_files]
    identity_files = [os.path.expanduser(item) for item in identity_files]

    allowed = {item.lower() for item in allowed_hosts}
    if allowed and requested_host.lower() not in allowed and hostname.lower() not in allowed:
        raise PermanentJobError("NAS host is not in the Ubuntu agent allowlist.")

    known_hosts = state_root / "known_hosts"
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.load_host_keys(str(known_hosts))
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    nas_key, nas_key_path = load_or_create_nas_key(state_root)
    connected_with_key = False

    common = {
        "hostname": hostname,
        "port": port,
        "username": username,
        "timeout": 20,
        "auth_timeout": 20,
        "banner_timeout": 20,
    }

    try:
        client.connect(
            **common,
            key_filename=[str(nas_key_path)] + identity_files,
            allow_agent=True,
            look_for_keys=True,
        )
        connected_with_key = True
    except paramiko.AuthenticationException:
        client.close()
        if not password:
            raise PermanentJobError(
                "NAS SSH key authentication failed and no password was supplied."
            )
        client = paramiko.SSHClient()
        client.load_system_host_keys()
        client.load_host_keys(str(known_hosts))
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(
            **common,
            password=password,
            allow_agent=False,
            look_for_keys=False,
        )

    client.save_host_keys(str(known_hosts))
    transport = client.get_transport()
    if transport is None:
        client.close()
        raise TransientJobError("NAS SSH transport is unavailable.")
    fingerprint = host_fingerprint(transport.get_remote_server_key())
    connection = {
        "hostname": hostname,
        "port": port,
        "username": username,
    }
    return client, fingerprint, connected_with_key, connection


def install_nas_public_key_via_ssh(
    client: paramiko.SSHClient,
    nas_key: paramiko.RSAKey,
) -> bool:
    public_line = nas_public_line(nas_key)
    encoded = base64.b64encode(public_line.encode("ascii")).decode("ascii")
    command = (
        "umask 077; "
        "mkdir -p ~/.ssh; "
        "touch ~/.ssh/authorized_keys; "
        f"key=$(printf %s {encoded} | base64 -d); "
        "if grep -qxF \"$key\" ~/.ssh/authorized_keys; then "
        "echo KEY_PRESENT; "
        "else printf \"%s\\n\" \"$key\" >> ~/.ssh/authorized_keys; echo KEY_ADDED; fi; "
        "chmod 700 ~/.ssh; chmod 600 ~/.ssh/authorized_keys"
    )
    _, stdout, stderr = client.exec_command(command, timeout=20)
    output = stdout.read().decode("utf-8", errors="replace")
    error = stderr.read().decode("utf-8", errors="replace")
    status_code = stdout.channel.recv_exit_status()
    if status_code != 0:
        raise PermanentJobError(
            "Unable to install the Ubuntu agent SSH key on the NAS account: "
            + (error.strip() or output.strip())
        )
    return "KEY_ADDED" in output


def manifest_path(
    state_root: Path,
    job: dict[str, Any],
) -> Path:
    identity = (
        str(job.get("client_id", ""))
        + "\0"
        + str(job.get("computer", ""))
        + "\0"
        + str(job.get("profile", ""))
    )
    name = hashlib.sha256(identity.encode("utf-8")).hexdigest() + ".json"
    return state_root / "manifests" / name


def load_manifest(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {"version": 1, "directories": [], "files": {}}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if int(data.get("version", 0)) != 1:
            raise ValueError("unsupported version")
        return data
    except Exception:
        return {"version": 1, "directories": [], "files": {}}


def scan_source(source: Path) -> tuple[list[str], dict[str, list[int]]]:
    directories: list[str] = []
    files: dict[str, list[int]] = {}
    for current, dir_names, file_names in os.walk(str(source), followlinks=False):
        current_path = Path(current)
        dir_names[:] = sorted(
            name
            for name in dir_names
            if not (current_path / name).is_symlink()
        )
        relative_dir = current_path.relative_to(source)
        if str(relative_dir) != ".":
            directories.append(PurePosixPath(*relative_dir.parts).as_posix())
        for name in sorted(file_names):
            path = current_path / name
            if path.is_symlink() or name == STATUS_FILE_NAME:
                continue
            relative = path.relative_to(source)
            key = PurePosixPath(*relative.parts).as_posix()
            item_stat = path.stat()
            files[key] = [int(item_stat.st_size), int(item_stat.st_mtime_ns)]
    return sorted(directories), files


def parent_directories(paths: list[str]) -> list[str]:
    parents: set[str] = set()
    for value in paths:
        pure = PurePosixPath(value)
        for parent in pure.parents:
            if str(parent) != ".":
                parents.add(parent.as_posix())
    return sorted(parents, key=lambda item: (item.count("/"), item))


def upload_tree_tar(
    client: paramiko.SSHClient,
    source: Path,
    remote_relative: PurePosixPath,
    state_root: Path,
    job: dict[str, Any],
) -> dict[str, int]:
    current_directories, current_files = scan_source(source)
    saved_path = manifest_path(state_root, job)
    previous = load_manifest(saved_path)
    previous_directories = set(previous.get("directories", []))
    previous_files = previous.get("files", {})
    changed_files = sorted(
        name
        for name, metadata in current_files.items()
        if previous_files.get(name) != metadata
    )
    new_directories = sorted(
        set(current_directories) - previous_directories,
        key=lambda item: (item.count("/"), item),
    )
    manifest = {
        "version": 1,
        "updated_utc": utc_now(),
        "directories": current_directories,
        "files": current_files,
    }
    archive_directories = sorted(
        set(new_directories + parent_directories(changed_files)),
        key=lambda item: (item.count("/"), item),
    )
    if not changed_files and not new_directories:
        atomic_text(
            saved_path,
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        )
        ensure_mode(saved_path, 0o600)
        return {
            "uploaded_files": 0,
            "skipped_files": len(current_files),
            "uploaded_bytes": 0,
        }

    remote = str(remote_relative)
    stage = ".ubuntu-win-share-staging/" + job["job_id"]
    command = (
        "umask 077; "
        + "rm -rf -- "
        + shlex.quote(stage)
        + "; mkdir -p -- "
        + shlex.quote(stage)
        + " "
        + shlex.quote(remote)
        + " && tar --no-same-owner -xpf - -C "
        + shlex.quote(stage)
        + " && rsync -a -- "
        + shlex.quote(stage + "/")
        + " "
        + shlex.quote(remote + "/")
        + "; status=$?; rm -rf -- "
        + shlex.quote(stage)
        + "; exit $status"
    )
    transport = client.get_transport()
    if transport is None:
        raise TransientJobError("NAS SSH transport is unavailable.")
    channel = transport.open_session(timeout=20)
    channel.settimeout(120)
    channel.exec_command(command)
    stream = channel.makefile_stdin("wb")
    try:
        with tarfile.open(
            fileobj=stream,
            mode="w|",
            format=tarfile.PAX_FORMAT,
        ) as archive:
            for relative in archive_directories:
                archive.add(
                    str(source / Path(*PurePosixPath(relative).parts)),
                    arcname=relative,
                    recursive=False,
                )
            for relative in changed_files:
                archive.add(
                    str(source / Path(*PurePosixPath(relative).parts)),
                    arcname=relative,
                    recursive=False,
                )
        stream.flush()
        channel.shutdown_write()
        stdout = channel.makefile("rb").read().decode("utf-8", errors="replace")
        stderr = channel.makefile_stderr("rb").read().decode(
            "utf-8",
            errors="replace",
        )
        exit_code = channel.recv_exit_status()
        if exit_code != 0:
            raise TransientJobError(
                "tar over SSH failed with exit code "
                + str(exit_code)
                + ": "
                + (stderr.strip() or stdout.strip())[-500:]
            )
    finally:
        stream.close()
        channel.close()

    atomic_text(saved_path, json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    ensure_mode(saved_path, 0o600)
    uploaded_bytes = sum(current_files[name][0] for name in changed_files)
    return {
        "uploaded_files": len(changed_files),
        "skipped_files": len(current_files) - len(changed_files),
        "uploaded_bytes": uploaded_bytes,
    }


def result_xml(
    job: dict[str, Any],
    status_value: str,
    message: str,
    stats: dict[str, int] | None = None,
    fingerprint: str = "",
    key_installed: bool = False,
) -> str:
    root = ET.Element("NasUploadResult", {"version": SCHEMA_VERSION})
    values = {
        "JobId": job.get("job_id", ""),
        "ClientId": job.get("client_id", ""),
        "Computer": job.get("computer", ""),
        "Profile": job.get("profile", ""),
        "Status": status_value,
        "UpdatedUtc": utc_now(),
        "Message": message,
        "HostKeySha256": fingerprint,
        "NasKeyInstalled": "true" if key_installed else "false",
        "UploadedFiles": str((stats or {}).get("uploaded_files", 0)),
        "SkippedFiles": str((stats or {}).get("skipped_files", 0)),
        "UploadedBytes": str((stats or {}).get("uploaded_bytes", 0)),
    }
    for name, value in values.items():
        ET.SubElement(root, name).text = value
    return ET.tostring(root, encoding="unicode") + "\n"


def result_name(job_path: Path) -> str:
    name = job_path.name
    return name[:-8] + ".result.xml" if name.endswith(".job.xml") else name + ".result.xml"


def write_result(
    results_root: Path,
    original_name: str,
    job: dict[str, Any],
    status_value: str,
    message: str,
    stats: dict[str, int] | None = None,
    fingerprint: str = "",
    key_installed: bool = False,
) -> None:
    atomic_text(
        results_root / result_name(Path(original_name)),
        result_xml(
            job,
            status_value,
            message,
            stats,
            fingerprint,
            key_installed,
        ),
    )


def process_job(
    job_path: Path,
    original_name: str,
    config: dict[str, Any],
    logger: logging.Logger,
) -> None:
    share_root = Path(config["share_root"]).resolve()
    state_root = Path(config["state_root"]).resolve()
    control_root = share_root / ".ubuntu-win-share"
    results_root = control_root / "jobs" / "results"
    failed_root = control_root / "jobs" / "failed"
    incoming_root = control_root / "jobs" / "incoming"
    job: dict[str, Any] = {}
    password = ""
    try:
        job = parse_job(job_path)
        source = validate_source(share_root, job["source_relative"])
        remote_path = safe_remote_path(
            validate_remote_path(job["nas_remote_path"])
        )
        private_key = load_or_create_encryption_key(state_root)
        password = decrypt_password(
            job,
            private_key,
            control_root / "keys" / PUBLIC_KEY_NAME,
        )
        client, fingerprint, connected_with_key, _connection = connect_client(
            job,
            state_root,
            password,
            list(config.get("allowed_hosts", [])),
        )
        try:
            nas_key, _ = load_or_create_nas_key(state_root)
            key_installed = False
            if not connected_with_key:
                key_installed = install_nas_public_key_via_ssh(client, nas_key)
            stats = upload_tree_tar(
                client,
                source,
                remote_path,
                state_root,
                job,
            )
        finally:
            client.close()

        write_result(
            results_root,
            original_name,
            job,
            "success",
            "NAS upload completed.",
            stats,
            fingerprint,
            key_installed,
        )
        job_path.unlink(missing_ok=True)
        logger.info(
            "job=%s status=success uploaded=%s skipped=%s bytes=%s",
            job["job_id"],
            stats["uploaded_files"],
            stats["skipped_files"],
            stats["uploaded_bytes"],
        )
    except (
        paramiko.AuthenticationException,
        paramiko.BadHostKeyException,
        PermanentJobError,
    ) as exc:
        message = safe_error(exc, password)
        if not job:
            job = {"job_id": job_path.stem}
        write_result(results_root, original_name, job, "failed", message)
        failed_name = failed_root / (
            time.strftime("%Y%m%d-%H%M%S") + "-" + original_name
        )
        failed_root.mkdir(parents=True, exist_ok=True)
        os.replace(str(job_path), str(failed_name))
        logger.error("job=%s status=failed error=%s", job.get("job_id", ""), message)
    except (
        paramiko.SSHException,
        paramiko.ssh_exception.NoValidConnectionsError,
        socket.timeout,
        TimeoutError,
        OSError,
        TransientJobError,
    ) as exc:
        message = safe_error(exc, password)
        if not job:
            job = {"job_id": job_path.stem}
        write_result(results_root, original_name, job, "retrying", message)
        destination = incoming_root / original_name
        if destination.exists():
            superseded = failed_root / (
                time.strftime("%Y%m%d-%H%M%S") + "-superseded-" + original_name
            )
            failed_root.mkdir(parents=True, exist_ok=True)
            os.replace(str(job_path), str(superseded))
        else:
            os.replace(str(job_path), str(destination))
        logger.exception(
            "job=%s status=retrying error=%s",
            job.get("job_id", ""),
            message,
        )


def recover_processing(control_root: Path) -> None:
    processing = control_root / "jobs" / "processing"
    incoming = control_root / "jobs" / "incoming"
    for path in sorted(processing.glob("*.job.xml")):
        destination = incoming / path.name
        if destination.exists():
            path.unlink(missing_ok=True)
        else:
            os.replace(str(path), str(destination))


def process_once(config: dict[str, Any], logger: logging.Logger) -> int:
    share_root = Path(config["share_root"]).resolve()
    control_root = share_root / ".ubuntu-win-share"
    incoming = control_root / "jobs" / "incoming"
    processing = control_root / "jobs" / "processing"
    recover_processing(control_root)
    processed = 0

    for path in sorted(incoming.glob("*.job.xml")):
        claimed = processing / path.name
        try:
            os.replace(str(path), str(claimed))
        except FileNotFoundError:
            continue
        process_job(claimed, path.name, config, logger)
        processed += 1
    return processed


def create_logger(state_root: Path) -> logging.Logger:
    logger = logging.getLogger("ubuntu-win-share-upload-agent")
    logger.setLevel(logging.INFO)
    if logger.handlers:
        return logger
    log_path = state_root / "agent.log"
    handler = RotatingFileHandler(
        str(log_path),
        maxBytes=1024 * 1024,
        backupCount=2,
        encoding="utf-8",
    )
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
    logger.addHandler(handler)
    return logger


def acquire_lock(state_root: Path):
    state_root.mkdir(parents=True, exist_ok=True)
    lock_handle = (state_root / "agent.lock").open("a+")
    try:
        fcntl.flock(lock_handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        lock_handle.close()
        return None
    return lock_handle


def serve(config: dict[str, Any]) -> int:
    state_root = Path(config["state_root"]).resolve()
    lock_handle = acquire_lock(state_root)
    if lock_handle is None:
        return 0
    logger = create_logger(state_root)
    logger.info("agent=start share_root=%s", config["share_root"])
    try:
        while True:
            try:
                process_once(config, logger)
            except Exception as exc:
                logger.exception("agent-loop error=%s", safe_error(exc))
            time.sleep(max(5, int(config.get("poll_seconds", 15))))
    finally:
        lock_handle.close()


def self_test() -> int:
    import tempfile

    root = Path(tempfile.mkdtemp(prefix="ubuntu-win-share-agent-self-test-"))
    try:
        share = root / "share"
        state = root / "state"
        config = initialize_runtime(share, state, ["nas.example"], 10)
        private_key = load_or_create_encryption_key(state)
        secret = b"NAS_TEST_SECRET"
        cipher = private_key.public_key().encrypt(
            secret,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA1()),
                algorithm=hashes.SHA1(),
                label=None,
            ),
        )
        restored = private_key.decrypt(
            cipher,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA1()),
                algorithm=hashes.SHA1(),
                label=None,
            ),
        )
        if restored != secret:
            raise RuntimeError("RSA OAEP self-test failed.")
        if not (share / ".ubuntu-win-share" / "keys" / PUBLIC_KEY_NAME).exists():
            raise RuntimeError("Public key was not published.")
        if config["allowed_hosts"] != ["nas.example"]:
            raise RuntimeError("Agent config self-test failed.")
        print("NAS_UPLOAD_AGENT_SELF_TEST_OK")
        return 0
    finally:
        shutil.rmtree(root, ignore_errors=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--share-root", default=str(Path.home() / "C" / "ubuntu-win"))
    parser.add_argument(
        "--state-root",
        default=str(Path.home() / ".local" / "share" / "ubuntu-win-share-agent"),
    )
    parser.add_argument("--config")
    parser.add_argument("--allowed-host", action="append", default=[])
    parser.add_argument("--poll-seconds", type=int, default=15)
    parser.add_argument("--init", action="store_true")
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.self_test:
        return self_test()

    state_root = Path(args.state_root).expanduser()
    config_path = Path(args.config).expanduser() if args.config else state_root / "config.json"
    if args.init:
        initialize_runtime(
            Path(args.share_root),
            state_root,
            args.allowed_host,
            args.poll_seconds,
        )
        print("NAS_UPLOAD_AGENT_INIT_OK")
        return 0

    if not config_path.exists():
        raise SystemExit("Agent config is missing. Run with --init first.")
    config = load_config(config_path)
    logger = create_logger(Path(config["state_root"]))
    if args.once:
        print(f"NAS_UPLOAD_AGENT_PROCESSED={process_once(config, logger)}")
        return 0
    return serve(config)


if __name__ == "__main__":
    raise SystemExit(main())
