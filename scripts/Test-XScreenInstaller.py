"""Verify the electron-builder native release without running Setup or XScreen."""
import argparse
import base64
import gzip
import hashlib
import json
import re
import struct
import zlib
from pathlib import Path
import pefile

parser = argparse.ArgumentParser()
parser.add_argument("--report", required=True, type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
release = root / "release"
if args.report.exists():
    raise RuntimeError("Use a fresh report path")
version = json.loads((root / "package.json").read_text())["version"]
setup = release / f"XScreen Setup {version}.exe"
data = setup.read_bytes()
pe = pefile.PE(data=data)
results = []
def check(ok, name):
    results.append({"name": name, "passed": bool(ok)})

check(data[:2] == b"MZ" and pe.OPTIONAL_HEADER.Subsystem == 2, "Setup is a Windows GUI executable")
header = data.find(b"\xef\xbe\xad\xdeNullsoftInst") - 4
check(header >= 0, "Setup has a native NSIS header")
fields = struct.unpack_from("<7I", data, header)
end = header + fields[-1]
check(end == len(data) and zlib.crc32(data[512:end-4]) == struct.unpack_from("<I", data, end-4)[0], "Embedded NSIS CRC validates the complete protected installer")
resources = []
for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
    if entry.id == 24:
        for child in entry.directory.entries:
            for language in child.directory.entries:
                item = language.data.struct
                resources.append(pe.get_data(item.OffsetToData, item.Size).decode("utf-8-sig"))
check(len(resources) == 1 and 'level="asInvoker"' in resources[0], "Setup requests normal user privileges")
versions = {k.decode(): v.decode() for group in pe.FileInfo for node in group if hasattr(node, "StringTable") for table in node.StringTable for k, v in table.entries.items()}
check(versions.get("ProductName") == "XScreen" and versions.get("ProductVersion", "").startswith(version), "Setup product and version resources match the application")
check(any(e.id == 3 for e in pe.DIRECTORY_ENTRY_RESOURCE.entries), "Setup contains its icon")

manifest = json.loads((root / f"artifacts/releases/v{version}/XScreen-{version}-files.json").read_text(encoding="utf-8-sig"))
expected = {f["path"][8:]: f for f in manifest["files"]}
payload = release / "win-unpacked"
actual = {p.relative_to(payload).as_posix(): p for p in payload.rglob("*") if p.is_file()}
check(set(expected) == set(actual) and len(actual) == 521, "win-unpacked contains exactly the previously verified native payload, with no Electron runtime")
for relative, item in expected.items():
    p = actual[relative]
    check(p.stat().st_size == item["length"] and hashlib.sha256(p.read_bytes()).hexdigest() == item["sha256"], "Native payload remains unchanged: " + relative)

metadata = (release / "latest.yml").read_text(encoding="utf-8-sig")
sha512 = base64.b64encode(hashlib.sha512(data).digest()).decode()
listed = re.findall(r"sha512:\s*(\S+)", metadata)
check(len(listed) == 2 and all(v == sha512 for v in listed), "latest.yml SHA512 values match the actual Setup executable")
check(f"version: {version}" in metadata and f"path: XScreen-Setup-{version}.exe" in metadata and f"size: {len(data)}" in metadata, "latest.yml version, upload filename and size are correct")
blockmap = json.loads(gzip.decompress((release / (setup.name + ".blockmap")).read_bytes()))
chunks_ok = True
covered = 0
for item in blockmap["files"]:
    offset = item["offset"]
    chunks_ok &= offset == covered and len(item["sizes"]) == len(item["checksums"])
    for size, checksum in zip(item["sizes"], item["checksums"]):
        expected_chunk = base64.b64decode(checksum)
        chunks_ok &= size > 0 and offset + size <= len(data) and hashlib.blake2b(data[offset:offset+size], digest_size=len(expected_chunk)).digest() == expected_chunk
        offset += size
    covered = offset
check(chunks_ok and covered == len(data), "Every blockmap chunk hash and byte range matches Setup")

debug = (release / "builder-debug.yml").read_text(encoding="utf-8-sig")
config = (root / "electron-builder.yml").read_text(encoding="utf-8-sig")
hooks = (root / "packaging/installer.nsh").read_text(encoding="utf-8-sig")
remove = (root / "packaging/generated-uninstall.nsh").read_text(encoding="utf-8-sig")
check("runAfterFinish: false" in config and "customFinishPage" not in hooks, "Finish page does not automatically run XScreen")
check("allowToChangeInstallationDirectory: true" in config and "oneClick: false" in config, "Standard assisted installer includes installation directory selection")
check("signAndEditExecutable: false" in config and "packElevateHelper: false" in config, "Native executable editing and added elevation helper are disabled")
check("customCheckAppRunning" in hooks and "StopIfXScreenRunning" in hooks and "taskkill" not in hooks.replace("default taskkill", ""), "Running application is rejected without automatic process termination")
check(not re.search(r"(?im)^\s*(Exec|ExecShell|ExecWait|nsExec::)\b", hooks), "Custom hooks do not launch the native application")
deletes = re.findall(r'^Delete "\$INSTDIR\\([^"\r\n]+)"$', remove, re.M)
check(set(deletes) == {p.replace("/", "\\") for p in expected} | {"Uninstall XScreen.exe"}, "Custom removal includes only owned payload files and its uninstaller")
check(not re.search(r"(?im)^\s*RMDir\s+/r\b", remove), "Custom removal does not recursively delete logs, settings or unowned files")
report = {"packager": "electron-builder 26.15.3", "version": version, "checks": len(results), "failures": sum(not r["passed"] for r in results), "payloadFiles": len(actual), "blockmapChunks": sum(len(f["sizes"]) for f in blockmap["files"]), "finalSetupExecuted": False, "applicationExecuted": False, "realMonitorReads": 0, "realMonitorWrites": 0, "results": results}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"electron-builder release checks: {report['checks']}, failures {report['failures']}, native payload {len(actual)} files")
raise SystemExit(1 if report["failures"] else 0)
