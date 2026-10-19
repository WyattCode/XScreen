"""Execute the shipped NSIS hook instructions in a fail-closed offline VM.

Only Win32/COM/registry/filesystem boundaries are fake; branches, loops, macros,
directory policy, shortcut choice and generated uninstall list come from source.
No NSIS binary, application or real registry/shortcut is executed or modified.
"""
import argparse
import copy
import json
import ntpath
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def tokens(line):
    return re.findall(r'"[^"]*"|\S+', line)
def key(path):
    return ntpath.normpath(path).casefold()
class Stopped(Exception): pass
class Returned(Exception): pass
class Broken(Exception): pass

class Program:
    def __init__(self, uninstall=False):
        self.uninstall = uninstall
        self.defines = {'XSCREEN_PREFIX': 'un.' if uninstall else '',
            'XSCREEN_BUILD_RESOURCES': str(ROOT / 'packaging'),
            'INSTALL_REGISTRY_KEY': 'Software\\XScreen-test', 'NSIS_MAX_STRLEN': '1024'}
        if uninstall: self.defines['BUILD_UNINSTALLER'] = ''
        self.macros = {}
        source = (ROOT / 'packaging/installer.nsh').read_text(encoding='utf-8-sig')
        for match in re.finditer(r'(?m)^!macro (\w+)([^\n]*)\n(.*?)^!macroend', source, re.S):
            self.macros[match[1]] = (tokens(match[2]), match[3].splitlines())
        functions = self.preprocess((ROOT / 'packaging/functions.nsh').read_text(encoding='utf-8-sig').splitlines())
        self.functions = {}
        for match in re.finditer(r'(?m)^Function (\S+)\n(.*?)^FunctionEnd', '\n'.join(functions), re.S):
            self.functions[match[1]] = self.compile(match[2].splitlines())
    def subst(self, text, parameters=None):
        values = self.defines | (parameters or {})
        return re.sub(r'\$\{(\w+)\}', lambda m: values.get(m[1], m[0]), text)
    def preprocess(self, lines, parameters=None):
        output, active, frames = [], True, []
        for raw in lines:
            line = self.subst(raw.strip(), parameters)
            if not line or line.startswith(';'): continue
            t = tokens(line)
            if t[0] in ('!ifdef', '!ifndef'):
                yes = t[1] in self.defines
                if t[0] == '!ifndef': yes = not yes
                frames.append((active, yes)); active = active and yes
            elif t[0] == '!else':
                parent, yes = frames[-1]; active = parent and not yes
            elif t[0] == '!endif':
                active = frames.pop()[0]
            elif not active: continue
            elif t[0] == '!include':
                name = t[1].strip('"').replace('\\', '/')
                output.extend(self.preprocess(Path(name).read_text(encoding='utf-8-sig').splitlines()))
            elif t[0] == '!insertmacro':
                args, body = self.macros[t[1]]
                values = {name: value.strip('"') for name, value in zip(args, t[2:])}
                output.extend(self.preprocess(body, values))
            else: output.append(line)
        if frames: raise AssertionError('unclosed preprocessor condition')
        return output
    def macro(self, name):
        args, body = self.macros[name]
        assert not args
        return self.compile(self.preprocess(body))
    @staticmethod
    def compile(lines):
        code, frames, labels = [], [], {}
        for line in lines:
            t = tokens(line)
            if not t: continue
            cmd = t[0]
            if cmd in ('${If}', '${IfNot}'):
                pos = len(code); code.append(['branch', [(t[1:], cmd == '${IfNot}')], None])
                frames.append({'branch': pos, 'ends': []})
            elif cmd in ('${AndIf}', '${AndIfNot}', '${OrIf}'):
                code[frames[-1]['branch']][1].append((t[1:], cmd == '${AndIfNot}', cmd == '${OrIf}'))
            elif cmd in ('${Else}', '${ElseIf}'):
                frame = frames[-1]
                frame['ends'].append(len(code)); code.append(['jump', None])
                code[frame['branch']][2] = len(code)
                if cmd == '${ElseIf}':
                    frame['branch'] = len(code); code.append(['branch', [(t[1:], False)], None])
                else: frame['branch'] = None
            elif cmd == '${EndIf}':
                frame = frames.pop()
                if frame['branch'] is not None: code[frame['branch']][2] = len(code)
                for end in frame['ends']: code[end][1] = len(code)
            elif cmd in ('${Do}', '${DoWhile}'):
                frame = {'loop': len(code), 'breaks': []}; frames.append(frame)
                if cmd == '${DoWhile}':
                    frame['condition'] = len(code); code.append(['branch', [(t[1:], False)], None])
            elif cmd == '${Break}':
                frame = next(f for f in reversed(frames) if 'loop' in f)
                frame['breaks'].append(len(code)); code.append(['jump', None])
            elif cmd == '${Loop}':
                frame = frames.pop(); code.append(['jump', frame['loop']])
                if 'condition' in frame: code[frame['condition']][2] = len(code)
                for pos in frame['breaks']: code[pos][1] = len(code)
            elif line.endswith(':'): labels[line[:-1]] = len(code)
            else: code.append(['op', t])
        if frames: raise AssertionError('unclosed runtime condition')
        return code, labels

class VM:
    def __init__(self, program):
        self.p = program
        self.v = {'$INSTDIR': r'D:\Apps\XScreen', '$WINDIR': r'C:\Windows',
                  '$DESKTOP': r'C:\User\Desktop', '$SMPROGRAMS': r'C:\User\StartMenu',
                  '$TEMP': r'C:\Temp', '$installMode': 'CurrentUser', '$DesktopChoice': '0'}
        self.files, self.dirs, self.links, self.registry = {}, {}, {}, {}
        self.locked, self.denied_links = set(), set()
        self.stack, self.handles = [], {}
        self.error, self.exitcode, self.mutex = False, 0, 2
        self.windows_build = 26300
        self.operations, self.messages = [], []
    def value(self, value):
        value = value.strip('"')
        return re.sub(r'\$[A-Za-z_0-9]+', lambda m: str(self.v.get(m[0], '')), value)
    def addfile(self, path, value='payload'):
        self.files[key(path)] = value
        parent = ntpath.dirname(path)
        while parent and key(parent) not in self.dirs:
            self.dirs[key(parent)] = 0x10
            up = ntpath.dirname(parent)
            if up == parent: break
            parent = up
    def reg(self, path):
        self.registry[('HKCU', r'Software\XScreen-test', 'InstallLocation')] = path
    def exists(self, path):
        return key(path) in self.files or key(path) in self.links or key(path) in self.dirs
    def condition(self, parts):
        if parts[0] == '${AtLeastBuild}': return self.windows_build >= int(parts[1])
        if parts[0] == '${Errors}': return self.error
        if parts[0] == '${FileExists}': return self.exists(self.value(parts[1]))
        a, op, b = self.value(parts[0]), parts[1], self.value(parts[2])
        if op in ('==', '!=', 'P<>'):
            same = a.casefold() == b.casefold()
            return same if op == '==' else not same
        if op == '>=': return int(a) >= int(b)
        raise AssertionError(parts)
    def run(self, compiled):
        code, labels = compiled; pc = 0; ticks = 0
        while pc < len(code):
            ticks += 1
            if ticks > 50000: raise AssertionError('runaway source loop')
            item = code[pc]; pc += 1
            if item[0] == 'jump': pc = item[1]; continue
            if item[0] == 'branch':
                yes = None
                for part in item[1]:
                    result = self.condition(part[0]) ^ part[1]
                    yes = result if yes is None else (yes or result if len(part) > 2 and part[2] else yes and result)
                if not yes: pc = item[2]
                continue
            t = item[1]; cmd = t[0]; a = [self.value(v) for v in t[1:]]
            if cmd == 'StrCpy':
                value = a[1]
                if len(a) > 3: value = value[int(a[3]):]
                if len(a) > 2 and a[2] != '': value = value[:int(a[2])]
                self.v[t[1]] = value
            elif cmd == 'GetFullPathName': self.v[t[1]] = ntpath.normpath(a[1]).rstrip('\\')
            elif cmd == '${GetFileName}': self.v[t[2]] = ntpath.basename(a[0])
            elif cmd == '${GetParent}': self.v[t[2]] = ntpath.dirname(a[0])
            elif cmd == 'IntOp': self.v[t[1]] = str(int(a[1], 0) & int(a[3], 0))
            elif cmd == 'ReadRegStr': self.v[t[1]] = self.registry.get(tuple(a[1:]), '')
            elif cmd == 'DeleteRegKey':
                self.registry = {k:v for k,v in self.registry.items() if k[:2] != tuple(a)}
            elif cmd == 'FindFirst':
                base = key(ntpath.dirname(a[2]))
                names = sorted({ntpath.basename(p) for p in self.files.keys() | self.dirs.keys() | self.links.keys() if key(ntpath.dirname(p)) == base})
                handle = str(len(self.handles)); self.handles[handle] = iter(names)
                self.v[t[1]] = handle; self.v[t[2]] = next(self.handles[handle], '')
            elif cmd == 'FindNext': self.v[t[2]] = next(self.handles[a[0]], '')
            elif cmd == 'FindClose': self.handles.pop(a[0], None)
            elif cmd == 'IfFileExists':
                dest = a[1] if self.exists(a[0]) else a[2]
                if dest != '0':
                    if dest.startswith('+'): pc += int(dest) - 1
                    else: pc = labels[dest]
            elif cmd == 'Call':
                if a[0].endswith('XScreenReadShortcut'):
                    # Fake the documented COM boundary; never use a real .lnk.
                    self.v['$XScreenLinkTarget'] = self.links.get(key(self.v['$XScreenLinkPath']), '')
                else: self.call(a[0])
            elif cmd == 'System::Call':
                api = ' '.join(t[1:])
                if 'OpenMutexW' in api:
                    self.v['$0'] = '1' if self.mutex == 0 else '0'; self.stack.append(str(self.mutex))
                elif 'CloseHandle' in api: pass
                elif 'GetFileAttributesW' in api:
                    path = self.value(re.search(r'\(w "([^"]+)"', api)[1])
                    self.v['$0'] = str(self.dirs.get(key(path), 0x80 if self.exists(path) else -1))
                else: raise AssertionError('Unmocked native call: '+api)
            elif cmd == 'Push': self.stack.append(a[0])
            elif cmd == 'Pop': self.v[t[1]] = self.stack.pop()
            elif cmd == 'MessageBox': self.messages.append(a[1])
            elif cmd == 'SetErrorLevel': self.exitcode = int(a[0])
            elif cmd == 'Abort': raise Stopped(self.messages[-1] if self.messages else 'Abort')
            elif cmd == 'Return': return
            elif cmd == 'ClearErrors': self.error = False
            elif cmd == 'Delete':
                path = key(a[0]); self.operations.append(('delete', path))
                if path in self.locked: self.error = True
                else: self.files.pop(path, None); self.links.pop(path, None)
            elif cmd == 'RMDir':
                path = key(a[0]); self.operations.append(('rmdir', path))
                children = [p for p in self.files.keys() | self.dirs.keys() | self.links.keys() if p.startswith(path+'\\')]
                if children: self.error = True
                else: self.dirs.pop(path, None)
            elif cmd == 'CreateDirectory': self.dirs[key(a[0])] = 0x10
            elif cmd == 'CreateShortCut':
                self.operations.append(('shortcut', key(a[0])))
                if key(a[0]) in self.denied_links: self.error = True
                else: self.links[key(a[0])] = a[1]
            elif cmd == 'SetOutPath': pass
            else: raise AssertionError('Unimplemented source instruction: '+str(t))
    def call(self, name): self.run(self.p.functions[name])
    def macro(self, name): self.run(self.p.macro(name))

def main(report):
    results = []
    def case(name, test):
        test(); results.append({'name': name, 'passed': True})
    def stopped(test):
        try: test()
        except Stopped: return
        raise AssertionError('Expected operation to stop')
    inst, uninst = Program(), Program(True)
    def directory(path, reject=False, prepare=lambda v: None, uninstall=False):
        vm = VM(uninst if uninstall else inst); vm.v['$INSTDIR'] = path; prepare(vm)
        action = lambda: vm.call(('un.' if uninstall else '')+'XScreenValidateDirectory')
        if reject: stopped(action)
        else: action()
        assert not vm.operations
    for path in (r'D:\Apps\XScreen', r'D:\中文 软件\XScreen', 'd:\\apps\\xscreen\\', r'D:\Apps\XScreen\..\XScreen'):
        case('accept dedicated local path '+path, lambda p=path: directory(p))
    for path in ('D:\\', r'..\XScreen', r'\\server\share\XScreen', r'C:\Windows\Apps\XScreen', r'D:\Apps\MyXScreenTools'):
        case('reject unsafe/ambiguous path '+path, lambda p=path: directory(p, True))
    case('reject unregistered nonempty target', lambda: directory(r'D:\Apps\XScreen', True, lambda v:v.addfile(r'D:\Apps\XScreen\personal.txt')))
    case('accept registered existing installation with logs', lambda: directory(r'D:\Apps\XScreen', prepare=lambda v:(v.reg(r'd:\apps\xscreen'), v.addfile(r'D:\Apps\XScreen\runtime-data\log.txt'))))
    case('reject junction ancestor', lambda: directory(r'D:\Apps\XScreen', True, lambda v:v.dirs.update({key(r'D:\Apps'):0x410})))
    case('reject nested payload junction', lambda: directory(r'D:\Apps\XScreen', True, lambda v:v.dirs.update({key(r'D:\Apps\Assets'):0x410, key(r'D:\Apps\XScreen\Assets'):0x410})))
    case('reject unregister/mismatched uninstall directory', lambda: directory(r'D:\Apps\XScreen', True, lambda v:v.reg(r'D:\Other\XScreen'), True))
    case('accept registered uninstall directory', lambda: directory(r'D:\Apps\XScreen', prepare=lambda v:v.reg(r'D:\Apps\XScreen'), uninstall=True))
    def legacy():
        v=VM(inst); v.addfile(r'D:\Apps\XScreen\XScreen.exe'); v.addfile(r'D:\Apps\XScreen\XScreen.Core.dll')
        v.registry[('HKCU',r'Software\XScreen\Install','InstallDir')] = r'D:\Apps\XScreen'
        v.call('XScreenValidateDirectory')
    case('accept matching handwritten installation', legacy)
    for state in (0, 5):
        def busy(s=state):
            v=VM(inst); v.mutex=s; stopped(lambda:v.macro('customInit')); assert v.exitcode==2
        case('running or inaccessible app mutex rejects mutation '+str(state),busy)
    def shortcuts(present, target, expected):
        v=VM(inst); v.reg(r'D:\Apps\XScreen')
        if present: v.links[key(r'C:\User\Desktop\XScreen.lnk')]=target
        v.macro('customInit'); assert v.v['$DesktopChoice']==str(expected)
    case('fresh desktop choice defaults off',lambda:shortcuts(False,'',0))
    case('upgrade preserves existing desktop choice',lambda:shortcuts(True,r'D:\Apps\XScreen\XScreen.exe',1))
    case('upgrade respects user-deleted desktop link',lambda:shortcuts(False,'',0))
    case('unrelated desktop link does not imply selection',lambda:shortcuts(True,r'D:\Other.exe',0))
    def collision(path):
        v=VM(inst); v.reg(r'D:\Old\XScreen'); v.v['$DesktopChoice']='1'
        v.links[key(path)]=r'D:\Other.exe'
        stopped(lambda:v.macro('customCheckAppRunning')); assert not v.operations
    case('reject start menu collision before old uninstall',lambda:collision(r'C:\User\StartMenu\XScreen\XScreen.lnk'))
    case('reject selected desktop collision before old uninstall',lambda:collision(r'C:\User\Desktop\XScreen.lnk'))
    def unchecked_legacy_collision():
        v=VM(inst); v.reg(r'D:\Old\XScreen'); v.v['$DesktopChoice']='0'
        v.links[key(r'C:\User\Desktop\XScreen.lnk')]=r'D:\Other.exe'
        stopped(lambda:v.macro('customCheckAppRunning')); assert not v.operations
    case('old uninstaller cannot delete unchecked unrelated desktop link',unchecked_legacy_collision)
    def old_junction():
        v=VM(inst); v.reg(r'D:\Old\XScreen'); v.dirs[key(r'D:\Old\XScreen\Assets')]=0x410
        stopped(lambda:v.macro('customCheckAppRunning')); assert not v.operations
    case('directory-changing upgrade validates old payload junctions too',old_junction)
    def link_reparse():
        v=VM(inst); v.dirs[key(r'C:\User\StartMenu\XScreen')]=0x410
        stopped(lambda:v.macro('customCheckAppRunning')); assert not v.operations
    case('reject redirected per-app shortcut folder',link_reparse)
    def wrong_mode():
        v=VM(inst); v.v['$installMode']='all'
        stopped(lambda:v.macro('customInit')); assert not v.operations
    case('reject machine-wide install before changing files',wrong_mode)
    def old_windows():
        v=VM(inst); v.windows_build=18363
        stopped(lambda:v.macro('customInit')); assert not v.operations
    case('reject unsupported Windows before changing files',old_windows)
    def install(choice):
        v=VM(inst); v.v['$DesktopChoice']=str(choice)
        v.links[key(r'C:\User\Desktop\XScreen.lnk')]=r'D:\Apps\XScreen\XScreen.exe'
        v.macro('customInstall')
        assert key(r'C:\User\StartMenu\XScreen\XScreen.lnk') in v.links
        assert (key(r'C:\User\Desktop\XScreen.lnk') in v.links)==bool(choice)
    case('install creates selected desktop and start menu links',lambda:install(1))
    case('unchecking desktop removes only owned old shortcut',lambda:install(0))
    def failed_link():
        v=VM(inst); v.denied_links.add(key(r'C:\User\StartMenu\XScreen\XScreen.lnk'))
        stopped(lambda:v.macro('customInstall')); assert v.exitcode==2
    case('shortcut creation failure is reported',failed_link)
    def legacy_migration(unrelated=False):
        v=VM(inst); path=v.v['$INSTDIR']; oldkey=r'Software\XScreen\Install'
        v.registry[('HKCU',oldkey,'InstallDir')]=path
        v.registry[('HKCU',r'Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen','DisplayName')]='XScreen'
        v.addfile(path+r'\Uninstall.exe')
        link=key(r'C:\User\StartMenu\XScreen\卸载 XScreen.lnk')
        v.links[link]=r'D:\Other.exe' if unrelated else path+r'\Uninstall.exe'
        v.macro('customInstall')
        assert key(path+r'\Uninstall.exe') not in v.files
        assert not any(k[1]==oldkey for k in v.registry)
        assert (link in v.links)==unrelated
    case('identical-path legacy migration removes obsolete registration and link',lambda:legacy_migration())
    case('legacy migration preserves unrelated uninstall shortcut',lambda:legacy_migration(True))
    manifest=json.loads((ROOT/'artifacts/releases/v0.3.29/XScreen-0.3.29-files.json').read_text(encoding='utf-8-sig'))
    def removal(locked=False, unrelated=False):
        v=VM(uninst); v.reg(v.v['$INSTDIR'])
        for item in manifest['files']: v.addfile(v.v['$INSTDIR']+'\\'+item['path'][8:].replace('/','\\'))
        v.addfile(v.v['$INSTDIR']+r'\uninstallerIcon.ico'); v.addfile(v.v['$INSTDIR']+r'\Uninstall XScreen.exe')
        retained=[v.v['$INSTDIR']+r'\runtime-data\log.json',v.v['$INSTDIR']+r'\personal.txt',r'C:\User\AppData\Local\XScreen\hotkeys.json']
        for p in retained: v.addfile(p,'keep')
        for p in (r'C:\User\Desktop\XScreen.lnk',r'C:\User\StartMenu\XScreen\XScreen.lnk'):
            v.links[key(p)] = r'D:\Other.exe' if unrelated else v.v['$INSTDIR']+r'\XScreen.exe'
        if locked:
            v.locked.add(key(v.v['$INSTDIR']+r'\XScreen.Core.dll'))
            stopped(lambda:v.macro('customRemoveFiles')); assert v.exitcode==2 and v.registry
            assert key(v.v['$INSTDIR']+r'\Uninstall XScreen.exe') in v.files
        else:
            v.macro('customRemoveFiles')
            assert all(v.files.get(key(p))=='keep' for p in retained)
            assert len(v.files)==len(retained)
            assert len(v.links)==(2 if unrelated else 0)
        assert all('runtime-data' not in p for op,p in v.operations)
    case('uninstall removes owned files/icon/links and preserves data',lambda:removal())
    case('uninstall preserves unrelated same-name shortcuts',lambda:removal(unrelated=True))
    case('locked payload stops uninstall before registry/shortcut removal',lambda:removal(locked=True))
    def repeated_removal():
        v=VM(uninst); v.reg(v.v['$INSTDIR'])
        v.macro('customRemoveFiles'); v.macro('customRemoveFiles')
        assert v.exitcode==0 and v.registry
    case('retry removal tolerates already missing payload files',repeated_removal)
    def moved_upgrade():
        v=VM(inst); old=r'D:\Old\XScreen'; v.reg(old)
        v.links[key(r'C:\User\Desktop\XScreen.lnk')]=old+r'\XScreen.exe'
        v.macro('customInit'); assert v.v['$DesktopChoice']=='1'
        v.macro('customCheckAppRunning')
        # Run the new uninstall hooks against the old path using the same fake OS.
        oldvm=VM(uninst); oldvm.reg(old); oldvm.v['$INSTDIR']=old
        oldvm.links=copy.deepcopy(v.links); oldvm.macro('customRemoveFiles')
        v.links=oldvm.links; v.macro('customInstall')
        assert v.links[key(r'C:\User\Desktop\XScreen.lnk')]==r'D:\Apps\XScreen\XScreen.exe'
    case('directory-changing upgrade retains choice and retargets links',moved_upgrade)
    # Structural checks supplement execution at native/template boundaries.
    no_start=(ROOT/'packaging/no-start.nsh').read_text(encoding='utf-8-sig')
    assert not re.search(r'Exec|ExecShell|ExecWait',no_start)
    results.append({'name':'force-run macro has no executable launch instruction','passed':True})
    report.parent.mkdir(parents=True,exist_ok=True)
    report.write_text(json.dumps({'cases':len(results),'failures':0,'mode':'NSIS source VM, mocked OS boundaries',
        'installerExecutions':0,'realRegistryWrites':0,'realMonitorReads':0,'realMonitorWrites':0,'results':results},ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Installer behavior regression: {len(results)} passed; installer executions 0; monitor reads/writes 0/0')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args()
    if args.report.exists(): raise RuntimeError('Use a fresh report path')
    main(args.report)
