// Compile with electron-builder, but NEVER execute the temporary Setup to obtain
// its uninstaller. Use the builder's static NSIS reader on every host instead.
const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { build, Platform, Arch } = require('electron-builder');
const { NsisTarget } = require('app-builder-lib/out/targets/nsis/NsisTarget');
const { UninstallerReader, nsisTemplatesDir } = require('app-builder-lib/out/targets/nsis/nsisUtil');
const { WineVmManager } = require('app-builder-lib/out/vm/WineVm');

async function main() {
  const root = path.resolve(__dirname, '..');
  if (require('app-builder-lib/package.json').version !== '26.15.3')
    throw new Error('Re-audit the static NSIS adapter before changing builder version');
  // Fail closed: no Wine/Windows VM fallback may launch a generated executable.
  WineVmManager.prototype.exec = async () => { throw new Error('Generated executable execution is forbidden'); };
  let extracted = 0;
  let uninstallerSha256;
  NsisTarget.prototype.computeScriptAndSignUninstaller = async function(defines, commands, installerPath, sharedHeader, archs) {
    if (this.options.script) throw new Error('Only the audited stock NSIS template is supported');
    const script = await fs.readFile(path.join(nsisTemplatesDir, 'installer.nsi'), 'utf8');
    const uninstaller = path.join(this.outDir, `${path.basename(installerPath, 'exe')}__uninstaller.exe`);
    defines.BUILD_UNINSTALLER = null;
    defines.UNINSTALLER_OUT_FILE = uninstaller;
    await this.executeMakensis(defines, commands, sharedHeader + await this.computeFinalScript(script, false, archs));
    await UninstallerReader.exec(installerPath, uninstaller);
    const bytes = await fs.readFile(uninstaller);
    if (!bytes.subarray(0, 2).equals(Buffer.from('MZ')))
      throw new Error('Static extraction did not produce a PE uninstaller');
    uninstallerSha256 = createHash('sha256').update(bytes).digest('hex');
    // Evidence only. .bin extension avoids offering a runnable uninstaller.
    await fs.writeFile(path.join(this.outDir, 'uninstaller-evidence.bin'), bytes);
    extracted++;
    delete defines.BUILD_UNINSTALLER;
    defines.UNINSTALLER_OUT_FILE = uninstaller;
    return { script, isCustomScript: false };
  };
  await build({projectDir: root, config: path.join(root, 'electron-builder.yml'),
    prepackaged: path.join(root, 'release/win-unpacked'),
    targets: Platform.WINDOWS.createTarget(['nsis'], Arch.x64), publish: 'never'});
  if (extracted !== 1) throw new Error('Expected exactly one statically extracted uninstaller');
  const sources = ['electron-builder.yml', 'packaging/installer.nsh', 'packaging/functions.nsh',
    'packaging/no-start.nsh', 'packaging/per-user.nsh', 'packaging/generated-paths.nsh',
    'packaging/generated-uninstall.nsh', 'scripts/Build-XScreenInstaller.cjs', 'scripts/Build-XScreenInstaller.ps1'];
  const sourceHashes = {};
  for (const name of sources) sourceHashes[name] = createHash('sha256').update(await fs.readFile(path.join(root, name))).digest('hex');
  await fs.writeFile(path.join(root, 'release/static-build.json'), JSON.stringify({
    builder: '26.15.3', uninstallerGeneration: 'UninstallerReader static extraction',
    generatedExecutablesExecuted: 0, extractedUninstallers: extracted, uninstallerSha256, sourceHashes
  }, null, 2));
}
main().catch(error => { console.error(error); process.exit(1); });
