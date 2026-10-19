"""Static installer checks only. Never execute the installer or installed application."""
import argparse
import hashlib
import json
import re
import struct
import zlib
from pathlib import Path
import pefile

parser = argparse.ArgumentParser()
parser.add_argument("--build", required=True, type=Path)
parser.add_argument("--report", required=True, type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
if args.report.exists():
    raise RuntimeError("Use a fresh report path")
build = json.loads((args.build / "build-report.json").read_text(encoding="utf-8-sig"))
release = root / "artifacts/releases" / ("v" + build["version"])
manifest = json.loads((release / f"XScreen-{build['version']}-files.json").read_text(encoding="utf-8-sig"))
exe = release / build["installer"]
data = exe.read_bytes()
pe = pefile.PE(data=data)
results = []

def check(ok, name):
    results.append({"name": name, "passed": bool(ok)})

check(data[:2] == b"MZ" and pe.OPTIONAL_HEADER.Subsystem == 2, "Installer is a Windows GUI PE executable")
check(zlib.crc32(data[512:-4]) == struct.unpack("<I", data[-4:])[0], "Embedded NSIS CRC matches every protected executable and payload byte")
check(hashlib.sha256(data).hexdigest() == build["sha256"], "Installer hash matches compiler build report")
manifests = []
for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
    if entry.id == 24:
        for resource in entry.directory.entries:
            for language in resource.directory.entries:
                item = language.data.struct
                manifests.append(pe.get_data(item.OffsetToData, item.Size).decode("utf-8-sig"))
check(len(manifests) == 1 and 'level="asInvoker"' in manifests[0] and 'uiAccess="false"' in manifests[0], "Built installer requests current-user privileges without elevation")
versions = {key.decode(): value.decode() for group in pe.FileInfo for node in group if hasattr(node, "StringTable") for table in node.StringTable for key, value in table.entries.items()}
check(versions.get("ProductName") == "XScreen" and versions.get("FileVersion") == build["version"] + ".0", "Executable product and version resources are correct")
check(any(entry.id == 3 for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries), "Executable includes icon resources")
template = (root / "packaging/XScreen.nsi").read_text(encoding="utf-8-sig")
install = (args.build / "install-files.nsh").read_text(encoding="utf-8-sig")
uninstall = (args.build / "uninstall-files.nsh").read_text(encoding="utf-8-sig")
sources = re.findall(r'^File "([^"\r\n]+)"$', install, re.M)
deletes = re.findall(r'^Delete "\$INSTDIR\\([^"\r\n]+)"$', uninstall, re.M)
expected = {f["path"][8:].replace("/", "\\"): f for f in manifest["files"]}
check(len(sources) == len(expected) == build["payloadFiles"] == 521, "Exactly the verified 521 payload files are compiled")
for relative, item in expected.items():
    source = release / "stage/XScreen" / relative.replace("\\", "/")
    check(str(source.resolve()) in sources and source.stat().st_size == item["length"] and hashlib.sha256(source.read_bytes()).hexdigest() == item["sha256"], "Input matches verified ZIP payload: " + relative)
check(set(deletes) == set(expected) and len(deletes) == len(expected), "Uninstaller removes only the exact owned payload file list")
check(not re.search(r'(?im)^\s*RMDir\s+/r\b', template + uninstall), "Uninstaller never recursively deletes installation or user-data directories")
check(not re.search(r'(?im)^\s*(Exec|ExecShell|ExecWait|nsExec::)\b', template) and "MUI_FINISHPAGE_RUN" not in template, "No installation, completion or uninstall action launches the application")
check(" HKLM " not in template and "CurrentVersion\\Run" not in template, "No machine-wide registry or startup registration")
check('InstallDir "$LOCALAPPDATA\\Programs\\XScreen"' in template and "RequestExecutionLevel user" in template, "Default destination is the current user's writable Programs directory")
check('Section /o "创建桌面快捷方式"' in template and 'CreateShortcut "$SMPROGRAMS\\XScreen\\XScreen.lnk"' in template, "Start-menu shortcut is included and desktop shortcut is optional")
check('OpenMutexW' in template and template.count('!insertmacro CheckNotRunning') == 2, "Install and uninstall both reject a running XScreen without terminating it")
check('WriteUninstaller "$INSTDIR\\Uninstall.exe"' in template and '"UninstallString"' in template, "Windows uninstall entry and uninstaller are generated")
report = {"version": build["version"], "checks": len(results), "failures": sum(not x["passed"] for x in results), "installerExecuted": False, "realMonitorReads": 0, "realMonitorWrites": 0, "results": results}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Installer static checks: {report['checks']}, failures {report['failures']}; installer not executed")
if report["failures"]:
    raise SystemExit(1)
