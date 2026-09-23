#!/usr/bin/env bash
# Drag'n Wash mod installer / uninstaller for Steam Deck and Linux.
# The same script ships with every mod; what is specific to the mod comes from
# mod-install.json next to it.
#
# Run it from the extracted release folder, in Desktop Mode:
#   bash install-steamdeck.sh              asks whether to install or uninstall
#   bash install-steamdeck.sh --install    install or update straight away
#   bash install-steamdeck.sh --uninstall  remove the mod straight away
#
# What it does:
#   1. finds Drag'n Wash in your Steam libraries
#   2. works out what is missing: the Linux build of BepInEx 5.4.23.5, and
#      Drag'n Wash ModFramework when the mod's zip does not carry it (then the
#      release the mod names in mod-install.json, and only the libraries the
#      mod uses)
#   3. says where it would connect and why, and asks, before the first
#      connection; nothing is downloaded when everything is there already
#   4. checks each download's SHA-256 (and ModFramework's size) against the
#      values in this script and in mod-install.json; until then the game
#      folder is not changed
#   5. sets executable_name="DragNWash" in run_bepinex.sh
#   6. copies ModFramework and its libraries (never over a newer copy), then
#      the mod, and applies the mod's choices; files it overwrites are saved
#      in BepInEx/DragNWash.Installer/backup first, and when a step fails
#      part way everything is put back
#   7. sets the Steam launch option ./run_bepinex.sh %command%
#      (Steam has to be closed for that; you are asked first)
#
# Options: --install  --uninstall  --choice <id>=<value>  --game-dir <path>
#          --yes (no questions, use defaults; installs unless --uninstall;
#          counts as agreeing to the downloads)
#          --remove-bepinex --remove-data (with --uninstall)
#          --close-steam (close Steam without asking when the launch option
#          has to change)  --no-launch-option (leave launch options alone)
#          --bepinex-zip <file> (use a local BepInEx zip, still checked)
#          --framework-zip <file> (use a ModFramework zip you downloaded
#          yourself, still checked against mod-install.json)
#          --no-download (never connect to the internet; stops when
#          something would have to be downloaded)
#          --ui en|ja|zh
set -euo pipefail

APP_ID=4739660
GAME_BIN="DragNWash"
MARKER=".bepinex-installed-by-dragnwash-installer"
OLD_MARKER=".bepinex-installed-by-dragnwash-localization"
BEPINEX_VERSION="5.4.23.5"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v$BEPINEX_VERSION/BepInEx_linux_x64_$BEPINEX_VERSION.zip"
BEPINEX_SHA256="e538560be65739f562519ab518a75f9c65b3f57f87457403ae7cde683c12dab7"
LAUNCH_OPTION="./run_bepinex.sh %command%"
# The same version as Install.exe (installer/AssemblyInfo.cs); both send it as
# their User-Agent, and nothing else about the player.
INSTALLER_VERSION="1.1.0"
USER_AGENT="DragNWash.Installer/$INSTALLER_VERSION"
FRAMEWORK_REPO="TomXV/dragnwash-modframework"
MAX_DOWNLOAD=$((20 * 1024 * 1024))
# Where an install keeps its staging folder and the backup of what it
# overwrote. BepInEx loads nothing from here.
INST_REL="BepInEx/DragNWash.Installer"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="$HERE/mod-install.json"
FRAMEWORK_PREFIX="DragNWash.ModFramework"
FRAMEWORK_PATCHER="DragNWash.ModFramework.Preloader.dll"
FRAMEWORK_LISTS="com.tomxv.dragnwash.modframework.disabled.txt com.tomxv.dragnwash.modframework.state.txt com.tomxv.dragnwash.modframework.uninstall.txt"

MODE=""
GAME_DIR=""
ASSUME_YES=0
REMOVE_BEPINEX=0
REMOVE_DATA=0
CLOSE_STEAM=0
LAUNCH_OPTIONS=1
LOCAL_ZIP=""
FRAMEWORK_ZIP=""
NO_DOWNLOAD=0
UI=""
WARNINGS=""
TX_OPEN=0
WORK=""
# Filled in along the way; some messages name them.
FW_VERSION="" FW_SHA256="" FW_SIZE=0 FW_HAVE="" FW_PAGE="" FW_URL=""
C_REPOS="" C_FILES="" WAIT_MIN="" CURL_RC="" HTTP_CODE="" BAD_WHAT="" BAD_PART="" ZIP_PATH=""
TX_UNDONE=0 TX_LEFT=0 TX_REPLACED=0 BACKUP="" BACKUP_REL="" STAGE=""
declare -A CHOICE=()

while [ $# -gt 0 ]; do
    case "$1" in
        --install) MODE=install ;;
        --uninstall) MODE=uninstall ;;
        --choice) pair="${2:-}"; CHOICE["${pair%%=*}"]="${pair#*=}"; shift ;;
        --lang) CHOICE[language]="${2:-}"; shift ;;  # the option earlier Localization scripts had
        --game-dir) GAME_DIR="${2:-}"; shift ;;
        --yes|-y) ASSUME_YES=1 ;;
        --remove-bepinex) REMOVE_BEPINEX=1 ;;
        --remove-data) REMOVE_DATA=1 ;;
        --close-steam) CLOSE_STEAM=1 ;;
        --no-launch-option) LAUNCH_OPTIONS=0 ;;
        --bepinex-zip) LOCAL_ZIP="${2:-}"; shift ;;
        --framework-zip) FRAMEWORK_ZIP="${2:-}"; shift ;;
        --no-download) NO_DOWNLOAD=1 ;;
        --ui) UI="${2:-}"; shift ;;
        -h|--help) sed -n '2,/^set -euo pipefail$/{/^#/p}' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

# ---------------------------------------------------------------- manifest --
# mod-install.json is read with Python 3, which SteamOS ships. The checks match
# the Windows installer: every path the manifest names stays inside the mod's
# own folders and files.
mf() {
    python3 - "$MANIFEST" "$@" <<'PY'
import json, os, re, sys
path, cmd, args = sys.argv[1], sys.argv[2], sys.argv[3:]

def fail(msg, code=3):
    print(msg, file=sys.stderr)
    sys.exit(code)

SIMPLE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._ \-]{0,99}$")
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$")
# schema 2 adds "framework": the ModFramework release the mod is built
# against, fetched when the mod's zip does not carry it. Its values end up in
# a URL and in file names, so they are held to digits, dots and hex.
SCHEMA = 2
VERSION = re.compile(r"^[0-9]{1,9}(\.[0-9]{1,9}){1,3}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
COMPONENT = re.compile(r"^DragNWash\.ModFramework(\.[A-Za-z0-9]{1,64})?$")
MAX_SIZE = 20 * 1024 * 1024

def framework(fw):
    if not isinstance(fw, dict):
        fail("mod-install.json: \"framework\" must be an object")
    v = fw.get("version")
    if not isinstance(v, str) or not VERSION.match(v):
        fail(f"mod-install.json: framework version \"{v}\" must be digits and dots, like 1.4.3")
    h = fw.get("sha256")
    if not isinstance(h, str) or not SHA256.match(h):
        fail("mod-install.json: framework sha256 must be 64 lowercase hex digits")
    s = fw.get("size")
    if type(s) is not int or not 0 < s <= MAX_SIZE:
        fail(f"mod-install.json: framework size must be a whole number of bytes from 1 to {MAX_SIZE}")
    needs = fw.get("needs") or {}
    if not isinstance(needs, dict):
        fail("mod-install.json: framework \"needs\" must be an object of folder name: minimum version")
    for k, mv in needs.items():
        if not COMPONENT.match(k):
            fail(f"mod-install.json: \"{k}\" in framework needs is not a ModFramework folder name")
        if not isinstance(mv, str) or not VERSION.match(mv):
            fail(f"mod-install.json: the minimum for {k} must be digits and dots, like 1.2.0")
    return {"version": v, "sha256": h, "size": s, "needs": needs}

def config_name(f):
    if not isinstance(f, str) or not SIMPLE.match(f) or ".." in f or not f.lower().endswith((".cfg", ".txt", ".json")) \
            or f.lower().startswith("bepinex") or f.startswith("com.tomxv.dragnwash.modframework"):
        fail(f"mod-install.json: \"{f}\" is not a config file name this installer accepts")
    return f

def load():
    try:
        m = json.load(open(path, encoding="utf-8-sig"))
    except ValueError as e:
        fail(f"mod-install.json is not valid JSON: {e}", 4)  # the script says so in the player's language
    except Exception as e:
        fail(f"mod-install.json could not be read: {e}")
    schema = m.get("schema")
    if type(schema) is int and schema > SCHEMA:
        fail(f"mod-install.json: schema {schema} is newer than this installer reads (1 to {SCHEMA}); use the installer from this mod's release", 5)
    if type(schema) is not int or not 1 <= schema <= SCHEMA:
        fail(f"mod-install.json: schema {schema} is not supported (this installer reads schema 1 to {SCHEMA})")
    # schema 1 has no framework block; one there is left alone, as before.
    if schema >= 2 and m.get("framework") is not None:
        m["framework"] = framework(m["framework"])
    else:
        m.pop("framework", None)
    if not str(m.get("name") or "").strip():
        fail("mod-install.json: \"name\" is required")
    plugins = [p for p in (m.get("plugins") or []) if str(p).strip()]
    if not plugins:
        fail("mod-install.json: \"plugins\" must name at least one folder")
    for p in plugins:
        if not SIMPLE.match(p) or p.endswith(".") or p.lower().startswith("dragnwash.modframework"):
            fail(f"mod-install.json: \"{p}\" is not a plugin folder name this installer accepts")
    m["plugins"] = plugins
    keep = []
    for k in m.get("keep") or []:
        parts = str(k).replace("\\", "/").strip("/").split("/")
        if len(parts) < 2 or any(s in ("", ".", "..") for s in parts) or parts[0] not in plugins:
            fail(f"mod-install.json: keep path \"{k}\" must be inside one of the mod's plugin folders")
        keep.append("/".join(parts))
    m["keep"] = keep
    m["configFiles"] = [config_name(f) for f in (m.get("configFiles") or []) if str(f).strip()]
    for c in m.get("choices") or []:
        if not ID.match(str(c.get("id") or "")):
            fail("mod-install.json: every choice needs an \"id\"")
        cfg = c.get("config") or {}
        config_name(cfg.get("file"))
        if not str(cfg.get("section") or "").strip() or not str(cfg.get("key") or "").strip() \
                or re.search(r"[\[\]\r\n]", cfg["section"]) or re.search(r"[=\r\n]", cfg["key"]):
            fail(f"mod-install.json: choice \"{c['id']}\" has an invalid config target")
        c["options"] = [o for o in (c.get("options") or []) if o and o.get("value") and "\n" not in o["value"]]
        if not c["options"]:
            fail(f"mod-install.json: choice \"{c['id']}\" has no options")
    m["choices"] = m.get("choices") or []
    return m

def cfg_get(file, section, key):
    if not os.path.exists(file):
        return None
    cur = None
    for raw in open(file, encoding="utf-8-sig"):
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1].strip()
        elif cur == section and "=" in line and line.split("=", 1)[0].strip() == key:
            return line.split("=", 1)[1].strip()
    return None

def cfg_set(file, section, key, value):
    os.makedirs(os.path.dirname(file), exist_ok=True)
    lines = open(file, encoding="utf-8-sig").read().splitlines() if os.path.exists(file) else []
    cur, at = None, -1
    for i, raw in enumerate(lines):
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1].strip()
            if cur == section:
                at = i
        elif cur == section and "=" in line and line.split("=", 1)[0].strip() == key:
            lines[i] = f"{key} = {value}"
            break
    else:
        if at >= 0:
            lines.insert(at + 1, f"{key} = {value}")
        else:
            if lines and lines[-1].strip():
                lines.append("")
            lines += [f"[{section}]", "", f"{key} = {value}"]
    open(file, "w", encoding="utf-8").write("\n".join(lines) + "\n")

def ui_match(options, locale):
    values = [o["value"] for o in options]
    for c in (locale, locale.split("-")[0]):
        for v in values:
            if v.lower() == c.lower():
                return v
    return None

m = load()
choices = {c["id"]: c for c in m["choices"]}
if cmd == "check":
    pass
elif cmd in ("name", "version"):
    print(m.get(cmd) or "")
elif cmd in ("plugins", "keep", "configFiles"):
    for v in m[cmd]:
        print(v)
elif cmd == "choices":
    for c in m["choices"]:
        print(c["id"])
elif cmd == "framework":   # version, sha256 and size, one per line; nothing without the block
    if m.get("framework"):
        fw = m["framework"]
        print(f"{fw['version']}\n{fw['sha256']}\n{fw['size']}")
elif cmd == "needs":   # <folder>\t<minimum> for each library the mod uses
    for k, v in ((m.get("framework") or {}).get("needs") or {}).items():
        print(f"{k}\t{v}")
elif cmd == "cfgfile":   # cfgfile <id>: the config file a choice writes
    print(choices[args[0]]["config"]["file"])
elif cmd == "label":   # label <id> <ui>
    lab = choices[args[0]].get("label") or {}
    print(lab.get(args[1]) or lab.get("en") or args[0])
elif cmd == "options":   # options <id>
    for o in choices[args[0]]["options"]:
        print(f"{o['value']}\t{o.get('name') or o['value']}")
elif cmd == "has":   # has <id> <value>
    sys.exit(0 if any(o["value"] == args[1] for o in choices[args[0]]["options"]) else 1)
elif cmd == "default":   # default <id> <game> <ui-locale>
    c = choices[args[0]]
    t = c["config"]
    existing = cfg_get(os.path.join(args[1], "BepInEx", "config", t["file"]), t["section"], t["key"])
    values = [o["value"] for o in c["options"]]
    if existing in values:
        print(existing)
    elif c.get("default") == "ui-language" and ui_match(c["options"], args[2]):
        print(ui_match(c["options"], args[2]))
    elif c.get("default") in values:
        print(c["default"])
    else:
        print(values[0])
elif cmd == "apply":   # apply <id> <game> <value>
    c = choices[args[0]]
    t = c["config"]
    cfg_set(os.path.join(args[1], "BepInEx", "config", t["file"]), t["section"], t["key"], args[2])
elif cmd == "copy":   # copy <dst>: the manifest as installed, for the Mods screen
    json.dump(m, open(args[0], "w", encoding="utf-8"), ensure_ascii=False, indent=2)
elif cmd == "prune":   # prune <game> <plugin>: delete the plugin folder except keep
    import shutil
    game, plugin = args
    root = os.path.join(game, "BepInEx", "plugins", plugin)
    keep = [k[len(plugin) + 1:] for k in m["keep"] if k.startswith(plugin + "/")]
    keep = [k for k in keep if os.path.exists(os.path.join(root, k))]
    if not os.path.isdir(root):
        sys.exit(0)
    if not keep:
        shutil.rmtree(root)
        sys.exit(0)
    def prune(d, rel):
        for name in os.listdir(d):
            child = f"{rel}/{name}" if rel else name
            full = os.path.join(d, name)
            if child in keep:
                continue
            if any(k.startswith(child + "/") for k in keep) and os.path.isdir(full) and not os.path.islink(full):
                prune(full, child)
            elif os.path.isdir(full) and not os.path.islink(full):
                shutil.rmtree(full)
            else:
                os.remove(full)
    prune(root, "")
    print("kept: " + ", ".join(keep))
else:
    fail(f"unknown command {cmd}")
PY
}

# ----------------------------------------------------------------- strings --
# Which language to talk in, and which option a "ui-language" choice preselects.
#   1. The desktop's language, when it is not English. Prompts exist in
#      English, Japanese and Chinese; for other languages only the
#      preselected option follows.
#   2. Otherwise Steam's own language. Desktop Mode on a Deck is English
#      unless someone changed it in System Settings, while Steam itself is
#      often set to the player's language, and that is what they see in
#      Gaming Mode.
#   3. English.
detect_ui() {
    local v
    for v in "${LC_ALL:-}" "${LC_MESSAGES:-}" "${LANGUAGE:-}" "${LANG:-}"; do
        case "$v" in
            ja*) echo "ja ja"; return ;;
            zh_TW*|zh_HK*|zh_MO*|zh-Hant*) echo "zh zh-Hant"; return ;;
            zh*) echo "zh zh-Hans"; return ;;
            ko*) echo "en ko"; return ;;
            de*) echo "en de"; return ;;
            fr*) echo "en fr"; return ;;
            es*) echo "en es"; return ;;
            pt_BR*|pt-BR*) echo "en pt-BR"; return ;;
            ru*) echo "en ru"; return ;;
            pl*) echo "en pl"; return ;;
            he*|iw*) echo "en he"; return ;;
            eo*) echo "en eo"; return ;;
        esac
    done
    local steam_lang
    steam_lang="$(sed -n 's/^[[:space:]]*"language"[[:space:]]*"\([^"]*\)".*/\1/p' "$HOME/.steam/registry.vdf" 2>/dev/null | head -1 || true)"
    case "$steam_lang" in
        japanese) echo "ja ja" ;;
        schinese) echo "zh zh-Hans" ;;
        tchinese) echo "zh zh-Hant" ;;
        koreana) echo "en ko" ;;
        german) echo "en de" ;;
        french) echo "en fr" ;;
        spanish|latam) echo "en es" ;;
        brazilian) echo "en pt-BR" ;;
        russian) echo "en ru" ;;
        polish) echo "en pl" ;;
        *) echo "en en" ;;
    esac
}
read -r DETECTED_UI UI_LOCALE < <(detect_ui)
[ -n "$UI" ] || UI="$DETECTED_UI"

t() {
    local key="$1"
    case "$UI:$key" in
        ja:nopayload) echo "Mod のファイルが見つかりません。zip を丸ごと展開して、その中でこのスクリプトを実行してください。" ;;
        zh:nopayload) echo "找不到 Mod 文件。请完整解压 zip，并在解压后的文件夹中运行此脚本。" ;;
        *:nopayload) echo "The mod files are missing. Extract the whole zip and run this script from inside it." ;;
        ja:badjson) echo "このスクリプトの隣にある mod-install.json を読めませんでした（JSON が正しくありません）。zip をもう一度ダウンロードして、丸ごと展開してください。" ;;
        zh:badjson) echo "无法读取此脚本旁边的 mod-install.json（JSON 无效）。请重新下载 zip 并完整解压。" ;;
        *:badjson) echo "mod-install.json next to this script could not be read (invalid JSON). Download the zip again and extract it whole." ;;
        ja:badmanifest) echo "このスクリプトの隣にある mod-install.json を使えませんでした。zip をもう一度ダウンロードして、丸ごと展開してください。理由:" ;;
        zh:badmanifest) echo "无法使用此脚本旁边的 mod-install.json。请重新下载 zip 并完整解压。原因：" ;;
        *:badmanifest) echo "mod-install.json next to this script could not be used. Download the zip again and extract it whole. The reason:" ;;
        ja:nopython) echo "python3 が見つかりません。SteamOS には標準で入っています。" ;;
        zh:nopython) echo "找不到 python3。SteamOS 默认自带。" ;;
        *:nopython) echo "python3 was not found. SteamOS includes it." ;;
        ja:nogame) echo "Drag'n Wash が見つかりません。--game-dir でゲームのフォルダを指定してください。" ;;
        zh:nogame) echo "找不到 Drag'n Wash。请用 --game-dir 指定游戏文件夹。" ;;
        *:nogame) echo "Drag'n Wash was not found. Pass the game folder with --game-dir." ;;
        ja:running) echo "ゲームが起動中です。先に終了してください。" ;;
        zh:running) echo "游戏正在运行。请先关闭游戏。" ;;
        *:running) echo "The game is running. Close it first." ;;
        ja:bep_have) echo "BepInEx: 導入済み" ;;
        zh:bep_have) echo "BepInEx：已安装" ;;
        *:bep_have) echo "BepInEx: already installed" ;;
        ja:bep_get) echo "BepInEx: ${BEPINEX_URL##*/} をダウンロード中..." ;;
        zh:bep_get) echo "BepInEx：正在下载 ${BEPINEX_URL##*/}..." ;;
        *:bep_get) echo "BepInEx: downloading ${BEPINEX_URL##*/} ..." ;;
        ja:bep_bad) echo "BepInEx のダウンロードが改ざんされているか壊れています（SHA-256 不一致）。中止します。" ;;
        zh:bep_bad) echo "下载的 BepInEx 已损坏或被篡改（SHA-256 不一致）。已中止。" ;;
        *:bep_bad) echo "The BepInEx download is corrupt or tampered with (SHA-256 mismatch). Stopping." ;;
        ja:bep_ok) echo "BepInEx: 検証 OK、展開しました" ;;
        zh:bep_ok) echo "BepInEx：校验通过，已解压" ;;
        *:bep_ok) echo "BepInEx: verified and unpacked" ;;
        ja:mod_ok) echo "Mod: ファイルをコピーしました" ;;
        zh:mod_ok) echo "Mod：文件已复制" ;;
        *:mod_ok) echo "Mod: files copied" ;;
        ja:lo_same) echo "起動オプション: 設定済み" ;;
        zh:lo_same) echo "启动选项：已设置" ;;
        *:lo_same) echo "Launch option: already set" ;;
        ja:lo_ask) echo "Steam の起動オプションを変更するため、Steam を一度終了します。Steam は起動中に起動オプションを上書きするので、終了しないと変更が反映されません。変更後に Steam を自動で起動し直します。

Steam を終了して続けますか？（「いいえ」の場合は、起動オプションを手動で変更してください）" ;;
        zh:lo_ask) echo "要修改 Steam 启动选项，需要先关闭 Steam。Steam 运行时会覆盖启动选项，不关闭则修改不会生效。修改后会自动重新启动 Steam。

关闭 Steam 并继续吗？（选择“否”则需要手动修改启动选项）" ;;
        *:lo_ask) echo "Steam will be closed briefly to change the launch option. Steam overwrites launch options while it runs, so the change only sticks with Steam closed. Steam is started again afterwards.

Close Steam and continue? (If not, change the launch option by hand.)" ;;
        ja:lo_manual) echo "起動オプションは手動で設定してください: Steam でゲームのプロパティ → 起動オプション に次を入力" ;;
        zh:lo_manual) echo "请手动设置启动选项：在 Steam 中打开游戏属性 → 启动选项，输入以下内容" ;;
        *:lo_manual) echo "Set the launch option by hand: in Steam, game Properties → Launch Options, enter" ;;
        ja:lo_done) echo "起動オプションを設定しました:" ;;
        zh:lo_done) echo "启动选项已设置：" ;;
        *:lo_done) echo "Launch option set:" ;;
        ja:attention) echo "【要確認】うまくいかなかった手順があります:" ;;
        zh:attention) echo "【请注意】有步骤未能完成：" ;;
        *:attention) echo "Something needs your attention:" ;;
        ja:steam_slow_remove) echo "Steam が終了しなかったため、起動オプションを変更できませんでした。" ;;
        zh:steam_slow_remove) echo "Steam 没有退出，无法修改启动选项。" ;;
        *:steam_slow_remove) echo "Steam did not exit, so the launch option could not be changed." ;;
        ja:lo_manual_remove) echo "起動オプションは手動で元に戻してください: Steam でゲームのプロパティ → 起動オプション から ./run_bepinex.sh を消す" ;;
        zh:lo_manual_remove) echo "请手动恢复启动选项：在 Steam 中打开游戏属性 → 启动选项，删除 ./run_bepinex.sh" ;;
        *:lo_manual_remove) echo "Restore the launch option by hand: in Steam, game Properties → Launch Options, remove ./run_bepinex.sh" ;;
        ja:lo_removed) echo "起動オプションを元に戻しました" ;;
        zh:lo_removed) echo "启动选项已恢复" ;;
        *:lo_removed) echo "Launch option restored" ;;
        ja:steam_wait) echo "Steam の終了を待っています..." ;;
        zh:steam_wait) echo "正在等待 Steam 退出..." ;;
        *:steam_wait) echo "Waiting for Steam to exit..." ;;
        ja:steam_slow) echo "Steam が終了しなかったため、起動オプションを設定できませんでした。" ;;
        zh:steam_slow) echo "Steam 没有退出，无法设置启动选项。" ;;
        *:steam_slow) echo "Steam did not exit, so the launch option could not be set." ;;
        ja:done) echo "完了しました。ゲームモードに戻って、Steam からゲームを起動してください。Mod はゲームの Options → Mods からもアンインストールできます。" ;;
        zh:done) echo "完成。请回到游戏模式，从 Steam 启动游戏。也可以在游戏的 Options → Mods 中卸载模组。" ;;
        *:done) echo "Done. Go back to Gaming Mode and start the game from Steam. Mods can also be uninstalled in the game: Options → Mods." ;;
        ja:keep) echo "セーブ履歴と Mod の作業ファイルは残しますか？" ;;
        zh:keep) echo "保留存档历史和模组的工作文件吗？" ;;
        *:keep) echo "Keep save history and the mod's working files?" ;;
        ja:rmbep) echo "BepInEx も削除しますか？（BepInEx を使う Mod はほかにありません）" ;;
        zh:rmbep) echo "同时删除 BepInEx 吗？（没有其他使用 BepInEx 的 Mod）" ;;
        *:rmbep) echo "Also remove BepInEx? (no other mod uses it)" ;;
        ja:bep_kept) echo "BepInEx: 他の Mod があるため残しました" ;;
        zh:bep_kept) echo "BepInEx：存在其他 Mod，已保留" ;;
        *:bep_kept) echo "BepInEx: kept, other mods use it" ;;
        ja:bep_removed) echo "BepInEx: 削除しました" ;;
        zh:bep_removed) echo "BepInEx：已删除" ;;
        *:bep_removed) echo "BepInEx: removed" ;;
        ja:fw_kept) echo "ModFramework: 他の Mod があるため残しました" ;;
        zh:fw_kept) echo "ModFramework：存在其他 Mod，已保留" ;;
        *:fw_kept) echo "ModFramework: kept, other mods are installed" ;;
        ja:fw_removed) echo "ModFramework: 削除しました" ;;
        zh:fw_removed) echo "ModFramework：已删除" ;;
        *:fw_removed) echo "ModFramework: removed" ;;
        ja:mod_removed) echo "Mod: 削除しました" ;;
        zh:mod_removed) echo "Mod：已删除" ;;
        *:mod_removed) echo "Mod: removed" ;;
        ja:undone) echo "アンインストールが完了しました。" ;;
        zh:undone) echo "卸载完成。" ;;
        *:undone) echo "Uninstall finished." ;;
        ja:confirm_install) echo "次のフォルダに Mod を導入します。よろしいですか？" ;;
        zh:confirm_install) echo "将把 Mod 安装到以下文件夹。继续吗？" ;;
        *:confirm_install) echo "Install the mod into this folder?" ;;
        ja:confirm_uninstall) echo "次のフォルダから Mod を削除します。よろしいですか？" ;;
        zh:confirm_uninstall) echo "将从以下文件夹删除 Mod。继续吗？" ;;
        *:confirm_uninstall) echo "Remove the mod from this folder?" ;;
        ja:action) echo "何をしますか？" ;;
        zh:action) echo "要执行什么操作？" ;;
        *:action) echo "What would you like to do?" ;;
        ja:act_install) echo "インストール / 更新" ;;
        zh:act_install) echo "安装 / 更新" ;;
        *:act_install) echo "Install / Update" ;;
        ja:act_uninstall) echo "アンインストール" ;;
        zh:act_uninstall) echo "卸载" ;;
        *:act_uninstall) echo "Uninstall" ;;
        ja:st_bep) echo "BepInEx" ;;
        zh:st_bep) echo "BepInEx" ;;
        *:st_bep) echo "BepInEx" ;;
        ja:st_mod) echo "Mod" ;;
        zh:st_mod) echo "Mod" ;;
        *:st_mod) echo "Mod" ;;
        ja:st_yes) echo "導入済み" ;;
        zh:st_yes) echo "已安装" ;;
        *:st_yes) echo "installed" ;;
        ja:st_no) echo "未導入" ;;
        zh:st_no) echo "未安装" ;;
        *:st_no) echo "not installed" ;;
        ja:nothing) echo "Mod は導入されていません。" ;;
        zh:nothing) echo "尚未安装 Mod。" ;;
        *:nothing) echo "The mod is not installed." ;;
        ja:newschema) echo "この Mod には新しいインストーラーが要ります。この Mod のリリース（Mod が入っていた zip）の install-steamdeck.sh を使ってください。" ;;
        zh:newschema) echo "此 Mod 需要更新的安装程序。请使用此 Mod 发布版（Mod 所在的 zip）中的 install-steamdeck.sh。" ;;
        *:newschema) echo "This mod needs a newer installer. Use the install-steamdeck.sh from this mod's release (the zip the mod came in)." ;;
        # Asking before the first connection: where, why, what is sent, and
        # how the file is checked. The same points as Install.exe's window.
        ja:c_head) echo "先にインターネットに接続します。" ;;
        zh:c_head) echo "将先连接互联网。" ;;
        *:c_head) echo "It connects to the internet first." ;;
        ja:c_fw) echo "この Mod には Drag'n Wash ModFramework $FW_VERSION が必要です。" ;;
        zh:c_fw) echo "此 Mod 需要 Drag'n Wash ModFramework $FW_VERSION。" ;;
        *:c_fw) echo "This mod needs Drag'n Wash ModFramework $FW_VERSION." ;;
        ja:c_fw_none) echo "ゲームのフォルダーにはありません。フレームワークの GitHub のリリースから取得します。" ;;
        zh:c_fw_none) echo "游戏文件夹中没有。将从框架的 GitHub 发布页获取。" ;;
        *:c_fw_none) echo "The game folder does not have it. It is fetched from the framework's GitHub releases." ;;
        ja:c_fw_have) echo "ゲームのフォルダーにあるのは $FW_HAVE です。フレームワークの GitHub のリリースから取得します。" ;;
        zh:c_fw_have) echo "游戏文件夹中是 $FW_HAVE。将从框架的 GitHub 发布页获取。" ;;
        *:c_fw_have) echo "The game folder has $FW_HAVE. It is fetched from the framework's GitHub releases." ;;
        ja:c_fw_part) echo "ゲームのフォルダーには $FW_HAVE がありますが、この Mod が使うライブラリが足りないか古いままです。フレームワークの GitHub のリリースから取得します。" ;;
        zh:c_fw_part) echo "游戏文件夹中有 $FW_HAVE，但此 Mod 使用的库缺失或过旧。将从框架的 GitHub 发布页获取。" ;;
        *:c_fw_part) echo "The game folder has $FW_HAVE, but a library this mod uses is missing or too old. It is fetched from the framework's GitHub releases." ;;
        ja:c_bep) echo "Mod を読み込む BepInEx $BEPINEX_VERSION が、ゲームのフォルダーにまだありません。" ;;
        zh:c_bep) echo "游戏文件夹中还没有用于加载 Mod 的 BepInEx $BEPINEX_VERSION。" ;;
        *:c_bep) echo "BepInEx $BEPINEX_VERSION, which loads the mods, is not in the game folder yet." ;;
        ja:c_to) echo "接続先:" ;;
        zh:c_to) echo "连接到：" ;;
        *:c_to) echo "Connects to:" ;;
        ja:c_gh) echo "github.com（$C_REPOS のリリース）" ;;
        zh:c_gh) echo "github.com（$C_REPOS 的发布页）" ;;
        *:c_gh) echo "github.com (releases of $C_REPOS)" ;;
        ja:c_ra) echo "release-assets.githubusercontent.com（GitHub がファイルを置いている場所。github.com から自動で転送されます）" ;;
        zh:c_ra) echo "release-assets.githubusercontent.com（GitHub 存放文件的地方，由 github.com 自动转到这里）" ;;
        *:c_ra) echo "release-assets.githubusercontent.com (where GitHub keeps the files; github.com forwards there by itself)" ;;
        ja:c_what) echo "目的: $C_FILES を 1 回だけ取得します。" ;;
        zh:c_what) echo "目的：只获取一次 $C_FILES。" ;;
        *:c_what) echo "Purpose: fetches $C_FILES, once." ;;
        ja:c_sent) echo "送るもの: 通常の HTTPS のリクエストと User-Agent $USER_AGENT だけです。名前、フォルダーの場所、ログイン情報、Cookie は送りません。どの Web サイトとも同じく、GitHub には IP アドレスが見えます。" ;;
        zh:c_sent) echo "发送内容：只有普通的 HTTPS 请求和 User-Agent $USER_AGENT。不发送姓名、文件夹位置、登录信息或 Cookie。与任何网站一样，GitHub 能看到你的 IP 地址。" ;;
        *:c_sent) echo "Sent: a plain HTTPS request and the User-Agent $USER_AGENT, nothing else. No name, folder location, login or cookies. As with any website, GitHub sees your IP address." ;;
        ja:c_check) echo "確認: SHA-256 がこの Mod のリリースに記録された値と一致したときだけ使います。それまでゲームのフォルダーは変わりません。" ;;
        zh:c_check) echo "校验：只有 SHA-256 与此 Mod 发布版中记录的值一致时才会使用。在此之前游戏文件夹不会有任何改动。" ;;
        *:c_check) echo "Check: a file is used only when its SHA-256 matches the value recorded in this mod's release. Until then the game folder is not changed." ;;
        ja:c_noapi) echo "api.github.com には接続しません。自分でダウンロードした zip を選べば、インターネットは使いません。" ;;
        zh:c_noapi) echo "不会连接 api.github.com。选择自己下载的 zip 则不使用互联网。" ;;
        *:c_noapi) echo "api.github.com is not contacted. With a zip you downloaded yourself, the internet is not used." ;;
        ja:c_page) echo "リリースのページ:" ;;
        zh:c_page) echo "发布页：" ;;
        *:c_page) echo "Release page:" ;;
        ja:c_download) echo "ダウンロードしてインストール" ;;
        zh:c_download) echo "下载并安装" ;;
        *:c_download) echo "Download and install" ;;
        ja:c_zip) echo "zip を選ぶ..." ;;
        zh:c_zip) echo "选择 zip..." ;;
        *:c_zip) echo "Choose a zip..." ;;
        ja:c_cancel) echo "キャンセル" ;;
        zh:c_cancel) echo "取消" ;;
        *:c_cancel) echo "Cancel" ;;
        ja:zip_fw) echo "ModFramework $FW_VERSION の zip（DragNWash.ModFramework-$FW_VERSION.zip）:" ;;
        zh:zip_fw) echo "ModFramework $FW_VERSION 的 zip（DragNWash.ModFramework-$FW_VERSION.zip）：" ;;
        *:zip_fw) echo "The ModFramework $FW_VERSION zip (DragNWash.ModFramework-$FW_VERSION.zip):" ;;
        ja:zip_bep) echo "BepInEx の zip（${BEPINEX_URL##*/}）:" ;;
        zh:zip_bep) echo "BepInEx 的 zip（${BEPINEX_URL##*/}）：" ;;
        *:zip_bep) echo "The BepInEx zip (${BEPINEX_URL##*/}):" ;;
        ja:zip_missing) echo "zip が見つかりません: $ZIP_PATH" ;;
        zh:zip_missing) echo "找不到 zip：$ZIP_PATH" ;;
        *:zip_missing) echo "The zip was not found: $ZIP_PATH" ;;
        ja:fw_get) echo "ModFramework: DragNWash.ModFramework-$FW_VERSION.zip をダウンロード中..." ;;
        zh:fw_get) echo "ModFramework：正在下载 DragNWash.ModFramework-$FW_VERSION.zip..." ;;
        *:fw_get) echo "ModFramework: downloading DragNWash.ModFramework-$FW_VERSION.zip ..." ;;
        ja:fw_ok) echo "ModFramework: $FW_VERSION 検証 OK（SHA-256）" ;;
        zh:fw_ok) echo "ModFramework：$FW_VERSION 校验通过（SHA-256）" ;;
        *:fw_ok) echo "ModFramework: $FW_VERSION verified (SHA-256)" ;;
        ja:fw_have) echo "ModFramework: $FW_HAVE 導入済み（この Mod にはこれで足ります）" ;;
        zh:fw_have) echo "ModFramework：已安装 $FW_HAVE，满足此 Mod 的需要" ;;
        *:fw_have) echo "ModFramework: $FW_HAVE installed, enough for this mod" ;;
        # Why a download failed. Each is followed by "The game folder was not changed."
        ja:e_offline) echo "インターネットにつながりません。" ;;
        zh:e_offline) echo "无法连接互联网。" ;;
        *:e_offline) echo "Could not connect to the internet." ;;
        ja:e_server) echo "GitHub が応答しませんでした（2 回試しました）。" ;;
        zh:e_server) echo "GitHub 没有响应（已尝试 2 次）。" ;;
        *:e_server) echo "GitHub did not answer, also on a second try." ;;
        ja:e_limit) echo "GitHub の利用制限に達しました。$WAIT_MIN 分後にもう一度試してください。" ;;
        zh:e_limit) echo "已达到 GitHub 的使用限制。请在 $WAIT_MIN 分钟后重试。" ;;
        *:e_limit) echo "GitHub's usage limit was reached. Try again in $WAIT_MIN minutes." ;;
        ja:e_limit_later) echo "GitHub の利用制限に達しました。しばらくしてからもう一度試してください。" ;;
        zh:e_limit_later) echo "已达到 GitHub 的使用限制。请稍后重试。" ;;
        *:e_limit_later) echo "GitHub's usage limit was reached. Try again later." ;;
        ja:e_notfound_fw) echo "ModFramework $FW_VERSION がまだ公開されていません。Mod の作者に知らせてください。" ;;
        zh:e_notfound_fw) echo "ModFramework $FW_VERSION 尚未发布。请告知 Mod 作者。" ;;
        *:e_notfound_fw) echo "ModFramework $FW_VERSION has not been published yet. Please tell the mod's author." ;;
        ja:e_notfound) echo "GitHub にファイルがありません（HTTP 404）。" ;;
        zh:e_notfound) echo "GitHub 上没有该文件（HTTP 404）。" ;;
        *:e_notfound) echo "The file is not on GitHub (HTTP 404)." ;;
        ja:e_tls) echo "安全な接続を確認できませんでした。" ;;
        zh:e_tls) echo "无法确认安全连接。" ;;
        *:e_tls) echo "A secure connection could not be confirmed." ;;
        ja:e_toobig) echo "ファイルが 20 MB を超えているため、使いませんでした。" ;;
        zh:e_toobig) echo "文件超过 20 MB，未使用。" ;;
        *:e_toobig) echo "The file is larger than 20 MB, so it was not used." ;;
        ja:e_other) echo "ダウンロードに失敗しました（curl エラー $CURL_RC、HTTP $HTTP_CODE）。" ;;
        zh:e_other) echo "下载失败（curl 错误 $CURL_RC，HTTP $HTTP_CODE）。" ;;
        *:e_other) echo "The download failed (curl error $CURL_RC, HTTP $HTTP_CODE)." ;;
        ja:bep_fail) echo "BepInEx をダウンロードできませんでした。" ;;
        zh:bep_fail) echo "无法下载 BepInEx。" ;;
        *:bep_fail) echo "BepInEx could not be downloaded." ;;
        ja:fw_bad_dl) echo "ダウンロードした ModFramework $FW_VERSION が、この Mod のリリースに記録されたものと一致しません（$BAD_WHAT）。ファイルは使わずに削除しました。" ;;
        zh:fw_bad_dl) echo "下载的 ModFramework $FW_VERSION 与此 Mod 发布版中记录的不一致（$BAD_WHAT）。文件未使用，已删除。" ;;
        *:fw_bad_dl) echo "The downloaded ModFramework $FW_VERSION does not match the one recorded in this mod's release ($BAD_WHAT). The file was deleted without being used." ;;
        ja:fw_bad_zip) echo "$ZIP_PATH は、この Mod のリリースに記録された ModFramework $FW_VERSION と一致しません（$BAD_WHAT）。使いませんでした。" ;;
        zh:fw_bad_zip) echo "$ZIP_PATH 与此 Mod 发布版中记录的 ModFramework $FW_VERSION 不一致（$BAD_WHAT）。未使用。" ;;
        *:fw_bad_zip) echo "$ZIP_PATH is not the ModFramework $FW_VERSION recorded in this mod's release ($BAD_WHAT). It was not used." ;;
        ja:nodl_fw) echo "この Mod には ModFramework $FW_VERSION が必要ですが、--no-download のため取得しません。" ;;
        zh:nodl_fw) echo "此 Mod 需要 ModFramework $FW_VERSION，但由于 --no-download 不会获取。" ;;
        *:nodl_fw) echo "This mod needs ModFramework $FW_VERSION, and --no-download does not allow fetching it." ;;
        ja:nodl_bep) echo "BepInEx が入っていませんが、--no-download のため取得しません。--bepinex-zip <ファイル> で zip を指定してください。" ;;
        zh:nodl_bep) echo "尚未安装 BepInEx，但由于 --no-download 不会获取。请用 --bepinex-zip <文件> 指定 zip。" ;;
        *:nodl_bep) echo "BepInEx is not installed, and --no-download does not allow fetching it. Pass the zip with --bepinex-zip <file>." ;;
        ja:unchanged) echo "ゲームのフォルダーは何も変わっていません。" ;;
        zh:unchanged) echo "游戏文件夹没有任何改动。" ;;
        *:unchanged) echo "The game folder was not changed." ;;
        ja:fw_manual) echo "リリースのページから zip を自分でダウンロードし、--framework-zip <ファイル> を付けてもう一度実行することもできます。同じように確かめます:" ;;
        zh:fw_manual) echo "也可以从发布页自行下载 zip，然后加上 --framework-zip <文件> 再次运行。同样会进行校验：" ;;
        *:fw_manual) echo "You can also download the zip yourself from the release page and run this script again with --framework-zip <file>. It is checked the same way:" ;;
        ja:fw_keep_ask) echo "ModFramework $FW_HAVE のまま、Mod だけ入れますか？（この Mod の最低条件は満たしています）" ;;
        zh:fw_keep_ask) echo "保留已安装的 ModFramework $FW_HAVE，只安装 Mod 吗？（它满足此 Mod 的最低要求）" ;;
        *:fw_keep_ask) echo "Keep the installed ModFramework $FW_HAVE and install only the mod? It meets this mod's minimums." ;;
        ja:fw_kept_old) echo "ModFramework $FW_VERSION は入れていません。入っている $FW_HAVE のままです（この Mod の最低条件は満たしています）。" ;;
        zh:fw_kept_old) echo "未安装 ModFramework $FW_VERSION。保留已安装的 $FW_HAVE（满足此 Mod 的最低要求）。" ;;
        *:fw_kept_old) echo "ModFramework $FW_VERSION was not installed. The installed $FW_HAVE is kept; it meets this mod's minimums." ;;
        ja:fw_short) echo "ModFramework $FW_VERSION には、この Mod が必要とする版の $BAD_PART がありません。Mod の作者に知らせてください。" ;;
        zh:fw_short) echo "ModFramework $FW_VERSION 中没有此 Mod 所需版本的 $BAD_PART。请告知 Mod 作者。" ;;
        *:fw_short) echo "ModFramework $FW_VERSION does not have $BAD_PART in the version this mod needs. Please tell the mod's author." ;;
        ja:unpack_failed) echo "zip を展開できませんでした: $BAD_PART" ;;
        zh:unpack_failed) echo "无法解压 zip：$BAD_PART" ;;
        *:unpack_failed) echo "The zip could not be unpacked: $BAD_PART" ;;
        ja:copy_failed) echo "コピーの途中で失敗しました（$BAD_PART）。" ;;
        zh:copy_failed) echo "复制中途失败（$BAD_PART）。" ;;
        *:copy_failed) echo "Copying failed part way ($BAD_PART)." ;;
        ja:stopped) echo "インストールが途中で止まりました。" ;;
        zh:stopped) echo "安装中途停止。" ;;
        *:stopped) echo "The install stopped part way." ;;
        ja:rolled_back) echo "すべて元に戻しました（$TX_UNDONE ファイル）。" ;;
        zh:rolled_back) echo "已全部恢复原状（$TX_UNDONE 个文件）。" ;;
        *:rolled_back) echo "Everything was put back ($TX_UNDONE files)." ;;
        ja:rollback_left) echo "$TX_LEFT 個のファイルを戻せませんでした。控えは $BACKUP_REL/files にあります。" ;;
        zh:rollback_left) echo "有 $TX_LEFT 个文件未能恢复。备份在 $BACKUP_REL/files。" ;;
        *:rollback_left) echo "$TX_LEFT files could not be put back. Their copies are in $BACKUP_REL/files." ;;
        ja:backup_done) echo "上書きした $TX_REPLACED ファイルの控え: $BACKUP_REL" ;;
        zh:backup_done) echo "已覆盖的 $TX_REPLACED 个文件的备份：$BACKUP_REL" ;;
        *:backup_done) echo "Backup of the $TX_REPLACED files overwritten: $BACKUP_REL" ;;
        *) echo "$key" ;;
    esac
}

# -------------------------------------------------------------- dialogs ----
GUI=0
if [ "$ASSUME_YES" -eq 0 ] && command -v kdialog >/dev/null 2>&1 && { [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }; then
    GUI=1
fi

LOG="${XDG_STATE_HOME:-$HOME/.local/state}/dragnwash-installer/installer.log"
mkdir -p "$(dirname "$LOG")" 2>/dev/null || true
log() { printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$LOG" 2>/dev/null || true; }
say() { echo "$*"; log "$*"; }
warn() { say "$*"; WARNINGS="${WARNINGS}${WARNINGS:+

}$*"; }
TITLE="Drag'n Wash mod installer"
fail() {
    local msg="$*"
    # A failure in the middle of an install puts back what it had changed
    # first, so the message can say how that went.
    if [ "$TX_OPEN" -eq 1 ]; then
        tx_rollback
        msg="$msg

$ROLLBACK_MSG"
    fi
    echo "ERROR: $msg" >&2
    log "ERROR: $msg"
    if [ "$GUI" -eq 1 ]; then kdialog --title "$TITLE" --error "$msg" >/dev/null 2>&1 || true; fi
    exit 1
}
# Anything else that stops the script half way (a command failing under
# set -e, Ctrl+C) is rolled back here. The download folder always goes.
on_exit() {
    if [ "$TX_OPEN" -eq 1 ]; then
        tx_rollback
        ROLLBACK_MSG="$(t stopped)
$ROLLBACK_MSG"
        echo "ERROR: $ROLLBACK_MSG" >&2
        log "ERROR: $ROLLBACK_MSG"
        if [ "$GUI" -eq 1 ]; then kdialog --title "$TITLE" --error "$ROLLBACK_MSG" >/dev/null 2>&1 || true; fi
    fi
    if [ -n "$WORK" ]; then rm -rf "$WORK"; fi
}
trap on_exit EXIT
trap 'exit 130' INT TERM HUP
ask_yes() {  # ask_yes "question" default(1=yes,0=no)
    local q="$1" def="${2:-1}"
    if [ "$ASSUME_YES" -eq 1 ]; then [ "$def" -eq 1 ]; return; fi
    if [ "$GUI" -eq 1 ]; then kdialog --title "$TITLE" --yesno "$q" >/dev/null 2>&1; return; fi
    local hint="[Y/n]"; [ "$def" -eq 0 ] && hint="[y/N]"
    local reply; read -r -p "$q $hint " reply || reply=""
    case "$reply" in
        [Yy]*) return 0 ;;
        [Nn]*) return 1 ;;
        *) [ "$def" -eq 1 ] ;;
    esac
}
finish_message() {
    local text="$1"
    if [ -n "$WARNINGS" ]; then
        text="$(t attention)

$WARNINGS

$1"
    fi
    say "$text"
    if [ "$GUI" -eq 1 ]; then
        if [ -n "$WARNINGS" ]; then
            kdialog --title "$TITLE" --sorry "$text" >/dev/null 2>&1 || true
        else
            kdialog --title "$TITLE" --msgbox "$text" >/dev/null 2>&1 || true
        fi
    fi
}

command -v python3 >/dev/null 2>&1 || fail "$(t nopython)"
[ -f "$MANIFEST" ] || fail "$(t nopayload)"
# A manifest that is there but cannot be used is a broken download, not a
# missing one; the reason in English goes to the log (and the dialog, when it
# is more than bad JSON).
mf_error="$(mf check 2>&1 >/dev/null)" && mf_rc=0 || mf_rc=$?
if [ "$mf_rc" -ne 0 ]; then
    log "$mf_error"
    [ "$mf_rc" -eq 4 ] && fail "$(t badjson)"
    [ "$mf_rc" -eq 5 ] && fail "$(t newschema)"
    fail "$(t badmanifest)
$mf_error"
fi
MOD_NAME="$(mf name)"
MOD_VERSION="$(mf version)"
mapfile -t PLUGINS < <(mf plugins)
TITLE="$MOD_NAME $MOD_VERSION (Steam Deck / Linux)"

# The ModFramework release the mod names (schema 2), with the minimum version
# of each library it uses. Checked again here before any of it goes into a URL.
mapfile -t fw_info < <(mf framework)
declare -A NEEDS=()
NEED_ORDER=()
if [ "${#fw_info[@]}" -eq 3 ]; then
    FW_VERSION="${fw_info[0]}" FW_SHA256="${fw_info[1]}" FW_SIZE="${fw_info[2]}"
    [[ "$FW_VERSION" =~ ^[0-9]{1,9}(\.[0-9]{1,9}){1,3}$ && "$FW_SHA256" =~ ^[0-9a-f]{64}$ &&
        "$FW_SIZE" =~ ^[1-9][0-9]{0,8}$ ]] && [ "$FW_SIZE" -le "$MAX_DOWNLOAD" ] ||
        fail "$(t badmanifest)
framework: version, sha256 or size"
    while IFS=$'\t' read -r name min; do
        [[ "$name" =~ ^DragNWash\.ModFramework(\.[A-Za-z0-9]+)?$ && "$min" =~ ^[0-9]{1,9}(\.[0-9]{1,9}){1,3}$ ]] ||
            fail "$(t badmanifest)
framework needs: $name"
        NEEDS[$name]="$min"
        NEED_ORDER+=("$name")
    done < <(mf needs)
    FW_URL="https://github.com/$FRAMEWORK_REPO/releases/download/v$FW_VERSION/DragNWash.ModFramework-$FW_VERSION.zip"
    FW_PAGE="https://github.com/$FRAMEWORK_REPO/releases/tag/v$FW_VERSION"
fi

# ------------------------------------------------------------ discovery ----
steam_roots() {
    local r
    for r in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.steam/root"; do
        [ -d "$r/steamapps" ] && readlink -f "$r"
    done | awk '!seen[$0]++'
}

library_paths() {
    local root vdf
    for root in $(steam_roots); do
        echo "$root"
        vdf="$root/steamapps/libraryfolders.vdf"
        [ -f "$vdf" ] && sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"
    done | awk '!seen[$0]++'
}

find_game() {
    local lib dir name
    while IFS= read -r lib; do
        [ -n "$lib" ] || continue
        name="Drag'n Wash"
        if [ -f "$lib/steamapps/appmanifest_$APP_ID.acf" ]; then
            name="$(sed -n 's/^[[:space:]]*"installdir"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$lib/steamapps/appmanifest_$APP_ID.acf" | head -1)"
        fi
        dir="$lib/steamapps/common/$name"
        if [ -f "$dir/$GAME_BIN" ]; then echo "$dir"; return 0; fi
    done < <(library_paths)
    return 1
}

steam_running() {
    local pid
    pid="$(cat "$HOME/.steam/steam.pid" 2>/dev/null || true)"
    [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null
}

# ------------------------------------------------------ launch options ----
# localconfig.vdf: UserLocalConfigStore > Software > Valve > Steam > apps > "<id>".
# Steam rewrites this file when it exits, so it must not be running.
vdf_tool() {  # vdf_tool <file> set|remove|check
    python3 - "$1" "$APP_ID" "$LAUNCH_OPTION" "$2" <<'PY'
import re, shutil, sys
path, app, option, action = sys.argv[1:5]
text = open(path, encoding="utf-8").read()

def find_block(text, key, start=0, end=None):
    """Return (open_brace_index, close_brace_index) of "key" { ... } within [start, end)."""
    end = len(text) if end is None else end
    for m in re.finditer(r'"%s"\s*\{' % re.escape(key), text[start:end]):
        o = start + m.end() - 1
        depth, i = 0, o
        while i < end:
            c = text[i]
            if c == '"':
                i += 1
                while i < end and text[i] != '"':
                    i += 2 if text[i] == '\\' else 1
            elif c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return o, i
            i += 1
    return None

blk = find_block(text, "Software")
blk = blk and find_block(text, "Valve", blk[0], blk[1])
blk = blk and find_block(text, "Steam", blk[0], blk[1])
blk = blk and find_block(text, "apps", blk[0], blk[1])
if not blk:
    sys.exit(1)
app_blk = find_block(text, app, blk[0], blk[1])

def indent_at(i):
    line_start = text.rfind("\n", 0, i) + 1
    return re.match(r"[\t ]*", text[line_start:]).group(0)

wrapped = option.replace(" %command%", "")   # ./run_bepinex.sh
if action == "check":
    if app_blk is None:
        sys.exit(4)
    body = text[app_blk[0]:app_blk[1]]
    m = re.search(r'"LaunchOptions"\s*"((?:[^"\\]|\\.)*)"', body)
    sys.exit(0 if (m and wrapped in m.group(1)) else 3)
if app_blk is None:
    if action == "remove":
        sys.exit(3)
    ind = indent_at(blk[1]) + "\t"
    insert = f'{ind}"{app}"\n{ind}{{\n{ind}\t"LaunchOptions"\t\t"{option}"\n{ind}}}\n'
    line_start = text.rfind("\n", 0, blk[1]) + 1
    new = text[:line_start] + insert + text[line_start:]
else:
    body = text[app_blk[0]:app_blk[1]]
    m = re.search(r'"LaunchOptions"\s*"((?:[^"\\]|\\.)*)"', body)
    current = m.group(1) if m else ""
    if action == "set":
        if wrapped in current:
            sys.exit(3)
        if "%command%" in current:
            value = current.replace("%command%", option, 1)
        elif current.strip():
            value = option + " " + current.strip()
        else:
            value = option
    else:
        if wrapped not in current:
            sys.exit(3)
        value = current.replace(option, "%command%", 1).replace(wrapped + " ", "", 1).strip()
        if value == "%command%":
            value = ""
    if m:
        s, e = app_blk[0] + m.start(1), app_blk[0] + m.end(1)
        new = text[:s] + value + text[e:]
    else:
        ind = indent_at(app_blk[1]) + "\t"
        line_start = text.rfind("\n", 0, app_blk[1]) + 1
        new = text[:line_start] + f'{ind}"LaunchOptions"\t\t"{value}"\n' + text[line_start:]

shutil.copy2(path, path + ".dragnwash-backup")
open(path, "w", encoding="utf-8").write(new)
sys.exit(0)
PY
}

edit_launch_options() {  # edit_launch_options set|remove ; 0 if any profile changed
    local action="$1" changed=0 vdf rc
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" "$action" || rc=$?
        case "$rc" in
            0) changed=1 ;;
            3|4) ;;        # nothing to change in this profile
            *) say "  (could not update $vdf)" ;;
        esac
    done
    [ "$changed" -eq 1 ]
}

launch_options_any() {  # yes when at least one profile has the wrapper
    local vdf rc
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" check || rc=$?
        [ "$rc" -eq 0 ] && { echo yes; return; }
    done
    echo no
}

launch_options_state() {  # yes when every profile that knows the game has the wrapper
    local vdf rc known=0 missing=0
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" check || rc=$?
        case "$rc" in
            0) known=1 ;;
            3) known=1; missing=1 ;;
        esac
    done
    if [ "$known" -eq 1 ] && [ "$missing" -eq 0 ]; then echo yes; else echo no; fi
}

start_steam() {
    # Start Steam in its own app-steam-*.scope. A plain child would stay in the
    # cgroup of whatever ran this script (Dolphin names it after the script),
    # and the desktop portal would then take Steam for install-steamdeck.sh and
    # ask "Share screen with" again instead of using Steam's saved permission.
    local unit
    unit="app-steam-$(od -An -N8 -tx8 /dev/urandom | tr -d ' \n').scope"
    if command -v systemd-run >/dev/null 2>&1 &&
        systemd-run --user --scope --quiet true >/dev/null 2>&1; then
        log "starting Steam in $unit"
        (nohup systemd-run --user --scope --quiet --slice=app.slice --unit="$unit" steam >/dev/null 2>&1 &) || true
    else
        log "systemd-run not usable; starting Steam directly"
        (nohup steam >/dev/null 2>&1 &) || true
    fi
}

with_steam_closed() {  # with_steam_closed set|remove ; returns 0 if applied
    local action="$1" was_running=0
    if steam_running; then
        log "Steam is running (pid $(cat "$HOME/.steam/steam.pid" 2>/dev/null)); launch option change needs it closed"
        if [ "$CLOSE_STEAM" -eq 1 ]; then
            log "closing Steam without asking (--close-steam)"
        elif ! ask_yes "$(t lo_ask)" 1; then
            log "user chose not to close Steam"
            return 2
        fi
        was_running=1
        log "running: steam -shutdown"
        steam -shutdown >/dev/null 2>&1 || true
        say "$(t steam_wait)"
        local i
        for i in $(seq 1 90); do
            steam_running || break
            sleep 1
        done
        if steam_running; then
            log "Steam still running after 90 s"
            return 3
        fi
        log "Steam exited"
        # Steam writes its config on the way out; give that a moment.
        sleep 3
    fi
    local rc=0
    edit_launch_options "$action" || rc=$?
    log "launch option $action: edit returned $rc"
    if [ "$was_running" -eq 1 ]; then
        log "starting Steam again"
        start_steam
    fi
    return "$rc"
}

# ------------------------------------------------------------------ main ----
log "---- start: $0 $* (mod=$MOD_NAME $MOD_VERSION, installer=$INSTALLER_VERSION, mode=${MODE:-ask}, ui=$UI, gui=$GUI)"
say "== $TITLE"

for p in "${PLUGINS[@]}"; do
    [ -d "$HERE/BepInEx/plugins/$p" ] || fail "$(t nopayload)"
done

if [ -z "$GAME_DIR" ]; then GAME_DIR="$(find_game || true)"; fi
GAME_DIR="${GAME_DIR%/}"
[ -n "$GAME_DIR" ] && [ -f "$GAME_DIR/$GAME_BIN" ] || fail "$(t nogame)"
say "Game: $GAME_DIR"
# Only a game started from this folder counts.
for pid in $(pgrep -x "$GAME_BIN" 2>/dev/null || true); do
    exe="$(readlink -f "/proc/$pid/exe" 2>/dev/null || true)"
    if [ -z "$exe" ] || [ "$exe" = "$(readlink -f "$GAME_DIR/$GAME_BIN")" ]; then fail "$(t running)"; fi
done

installed_version() {
    local folder="$GAME_DIR/BepInEx/plugins/${PLUGINS[0]}"
    if [ -f "$folder/mod-install.json" ]; then
        MANIFEST="$folder/mod-install.json" mf version 2>/dev/null && return
    fi
    ls "$folder"/*.dll >/dev/null 2>&1 && echo "?"
}

if [ -z "$MODE" ]; then
    have_bep="$(t st_no)"; [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ] && have_bep="$(t st_yes)"
    have_mod="$(installed_version || true)"; [ -n "$have_mod" ] || have_mod="$(t st_no)"
    status="$(t st_bep): $have_bep    $(t st_mod): $have_mod"
    if [ "$ASSUME_YES" -eq 1 ]; then
        MODE=install
    elif [ "$GUI" -eq 1 ]; then
        MODE="$(kdialog --title "$TITLE" --menu "$GAME_DIR
$status

$(t action)" install "$(t act_install)" uninstall "$(t act_uninstall)")" || exit 1
    else
        say "$status"
        say "$(t action)"
        say "  1) $(t act_install)"
        say "  2) $(t act_uninstall)"
        read -r -p "> " pick || pick=""
        case "$pick" in
            2) MODE=uninstall ;;
            1|"") MODE=install ;;
            *) exit 1 ;;
        esac
    fi
fi

# FileVersion of a .NET DLL (from its version resource), or empty.
dll_version() {
    [ -f "$1" ] || return 0
    python3 - "$1" <<'PY' 2>/dev/null || true
import re, sys
data = open(sys.argv[1], "rb").read()
z = b"\x00"
key = "FileVersion".encode("utf-16-le")
m = re.search(re.escape(key) + z + b"+((?:[0-9.]" + z + b")+)", data)
if m:
    print(m.group(1).decode("utf-16-le"))
PY
}

# Compares two versions part by part, a missing part counting as 0, so 1.4.3
# and 1.4.3.0 are the same. Prints -1, 0 or 1.
ver_cmp() {
    local -a x y
    local i a b
    IFS=. read -r -a x <<< "$1"
    IFS=. read -r -a y <<< "$2"
    for i in 0 1 2 3; do
        a="${x[i]:-0}" b="${y[i]:-0}"
        [[ "$a" =~ ^[0-9]{1,9}$ ]] || a=0
        [[ "$b" =~ ^[0-9]{1,9}$ ]] || b=0
        if ((10#$a > 10#$b)); then echo 1; return; fi
        if ((10#$a < 10#$b)); then echo -1; return; fi
    done
    echo 0
}

# 0 when version $1 is newer than $2.
version_newer() {
    [ -n "$1" ] && [ -n "$2" ] && [ "$(ver_cmp "$1" "$2")" = 1 ]
}

# ------------------------------------------------------------ transaction --
# Every change an install makes to the game folder goes through here. A file
# about to be overwritten or deleted is copied to
# BepInEx/DragNWash.Installer/backup/<time>/files first, and each step is
# written down (journal.txt next to it), so a failure part way can put back
# the old files and delete the new ones. Paths are relative to the game folder.
JOURNAL=()
declare -A TX_SEEN=()
ROLLBACK_MSG=""

tx_note() {  # tx_note new|replaced|dir <path>
    JOURNAL+=("$1"$'\t'"$2")
    if [ -n "$BACKUP" ] && [ -d "$BACKUP" ]; then printf '%s\t%s\n' "$1" "$2" >> "$BACKUP/journal.txt"; fi
}

tx_mkdir() {  # tx_mkdir <folder>: creates it and any missing parent, noting each
    local rel="$1" missing=()
    while [ -n "$rel" ] && [ "$rel" != . ] && [ ! -d "$GAME_DIR/$rel" ]; do
        missing=("$rel" "${missing[@]}")
        rel="$(dirname "$rel")"
    done
    for rel in "${missing[@]}"; do
        mkdir "$GAME_DIR/$rel" || return 1
        tx_note dir "$rel"
    done
}

tx_begin() {
    local stamp
    stamp="$(date '+%Y-%m-%d_%H%M%S')"
    JOURNAL=() TX_SEEN=() TX_REPLACED=0
    STAGE="$GAME_DIR/$INST_REL/staging"
    BACKUP_REL="$INST_REL/backup/$stamp"
    BACKUP=""
    rm -rf "$STAGE"   # left behind by a run that was killed
    TX_OPEN=1
    tx_mkdir "$INST_REL/staging" && tx_mkdir "$BACKUP_REL/files" || { BAD_PART="$INST_REL"; fail "$(t copy_failed)"; }
    BACKUP="$GAME_DIR/$BACKUP_REL"
    printf '%s\n' "${JOURNAL[@]}" > "$BACKUP/journal.txt"
    log "install started; backup and journal in $BACKUP"
}

tx_save() {  # tx_save <path>: about to change, replace or delete it; keep a copy first
    local rel="$1" dst="$GAME_DIR/$1"
    [ "$TX_OPEN" -eq 1 ] && [ -z "${TX_SEEN[$rel]:-}" ] || return 0
    TX_SEEN[$rel]=1
    if [ -e "$dst" ] || [ -L "$dst" ]; then
        mkdir -p "$(dirname "$BACKUP/files/$rel")" && cp -PpT "$dst" "$BACKUP/files/$rel" || return 1
        tx_note replaced "$rel"
        TX_REPLACED=$((TX_REPLACED + 1))
    else
        tx_mkdir "$(dirname "$rel")" || return 1
        tx_note new "$rel"
    fi
}

tx_copy() {  # tx_copy <source file> <path>: puts one file in place unless it is the same already
    local dst="$GAME_DIR/$2"
    if [ -f "$dst" ] && [ ! -L "$dst" ] && cmp -s "$1" "$dst"; then return 0; fi
    tx_save "$2" && cp -fT "$1" "$dst"
}

tx_copy_tree() {  # tx_copy_tree <source folder> <folder>: every file under it, over what is there
    local src="${1%/}" f
    tx_mkdir "$2" || { BAD_PART="$2"; return 1; }
    while IFS= read -r -d '' f; do
        f="${f#"$src"/}"
        tx_copy "$src/$f" "${2:+$2/}$f" || { BAD_PART="${2:+$2/}$f"; return 1; }
    done < <(find "$src" -type f -print0 | sort -z)
}

tx_rollback() {  # puts back everything in the journal; ROLLBACK_MSG says how that went
    local i kind rel
    TX_OPEN=0 TX_UNDONE=0 TX_LEFT=0
    for ((i = ${#JOURNAL[@]} - 1; i >= 0; i--)); do
        kind="${JOURNAL[i]%%$'\t'*}" rel="${JOURNAL[i]#*$'\t'}"
        case "$kind" in
            replaced)
                if cp -PpfT "$BACKUP/files/$rel" "$GAME_DIR/$rel" 2>/dev/null; then
                    TX_UNDONE=$((TX_UNDONE + 1))
                else
                    TX_LEFT=$((TX_LEFT + 1)); log "rollback: could not put back $rel"
                fi ;;
            new)
                if rm -f "$GAME_DIR/$rel" 2>/dev/null; then
                    TX_UNDONE=$((TX_UNDONE + 1))
                else
                    TX_LEFT=$((TX_LEFT + 1)); log "rollback: could not delete $rel"
                fi ;;
        esac
    done
    rm -rf "$STAGE"
    [ "$TX_LEFT" -ne 0 ] || rm -rf "$BACKUP"
    # Folders this run created, innermost first; only empty ones go.
    for ((i = ${#JOURNAL[@]} - 1; i >= 0; i--)); do
        kind="${JOURNAL[i]%%$'\t'*}" rel="${JOURNAL[i]#*$'\t'}"
        if [ "$kind" = dir ]; then rmdir "$GAME_DIR/$rel" 2>/dev/null || true; fi
    done
    log "rollback: $TX_UNDONE files put back or deleted, $TX_LEFT left"
    if [ "$TX_LEFT" -ne 0 ]; then
        ROLLBACK_MSG="$(t rollback_left)"
    elif [ "$TX_UNDONE" -eq 0 ]; then
        ROLLBACK_MSG="$(t unchanged)"
    else
        ROLLBACK_MSG="$(t rolled_back)"
    fi
}

tx_commit() {  # the install went through: the staging folder goes, one backup is kept
    local old
    rm -rf "$STAGE"
    for old in "$GAME_DIR/$INST_REL/backup"/*; do
        [ "$old" = "$BACKUP" ] || rm -rf "$old"
    done
    TX_OPEN=0
    log "install finished; ${#JOURNAL[@]} steps, $TX_REPLACED files backed up in $BACKUP"
}

# Forgets what the framework recorded about a plugin folder: switched off,
# renamed by the patcher, or waiting to be uninstalled.
forget_folder() {
    local folder="$1" list f
    for list in $FRAMEWORK_LISTS; do
        f="$GAME_DIR/BepInEx/config/$list"
        [ -f "$f" ] || continue
        awk -F '\t' -v p="$folder/" -v d="$folder" 'index($1, p) != 1 && $1 != d' "$f" > "$f.tmp"
        if cmp -s "$f" "$f.tmp"; then rm -f "$f.tmp"; continue; fi
        tx_save "BepInEx/config/$list" || { rm -f "$f.tmp"; BAD_PART="BepInEx/config/$list"; fail "$(t copy_failed)"; }
        mv -f "$f.tmp" "$f"
    done
}

# Installing means wanting the plugin on: drop copies the Mods screen switched off.
enable_folder() {
    local folder="$1" off
    while IFS= read -r -d '' off; do
        if [ -f "${off%.disabled}" ]; then
            tx_save "${off#"$GAME_DIR"/}" || { BAD_PART="${off#"$GAME_DIR"/}"; fail "$(t copy_failed)"; }
            rm -f "$off"
        fi
    done < <(find "$GAME_DIR/BepInEx/plugins/$folder" -name '*.dll.disabled' -print0 2>/dev/null)
    forget_folder "$folder"
}

# --------------------------------------------------------------- download --
# Fetches one file from a GitHub release: HTTPS only (redirects too), one
# retry for a timeout or a server error, nothing over 20 MB, and the
# installer's User-Agent. On failure FETCH_ERR says why, for the message.
FETCH_ERR=""
fetch() {  # fetch <url> <file>
    local url="$1" out="$2" rc=0 progress=(-sS)
    [ -t 2 ] && progress=(--progress-bar)   # in a terminal curl draws its own bar
    HTTP_CODE="$(curl -fL "${progress[@]}" --proto =https --proto-redir =https --retry 1 \
        --connect-timeout 30 --max-time 600 --max-filesize "$MAX_DOWNLOAD" -A "$USER_AGENT" \
        -D "$out.headers" -o "$out" -w '%{http_code}' "$url")" || rc=$?
    [ "$rc" -eq 0 ] && return 0
    CURL_RC="$rc"
    case "$rc" in
        5|6|7) FETCH_ERR=offline ;;
        28|52|56) FETCH_ERR=server ;;
        22) case "$HTTP_CODE" in
                404|410) FETCH_ERR=notfound ;;
                403|429) FETCH_ERR=limit ;;
                5??) FETCH_ERR=server ;;
                *) FETCH_ERR=other ;;
            esac ;;
        35|51|53|54|58|59|60|64|66|77|80|82|83|90|91) FETCH_ERR=tls ;;
        63) FETCH_ERR=toobig ;;
        *) FETCH_ERR=other ;;
    esac
    log "download failed: $url (curl exit $rc, HTTP $HTTP_CODE, $FETCH_ERR)"
    if [ "$FETCH_ERR" = limit ]; then
        # GitHub says how long to wait in one of these; minutes, rounded up.
        local s
        s="$(tr -d '\r' < "$out.headers" 2>/dev/null | sed -n 's/^retry-after:[[:space:]]*\([0-9]\+\).*/\1/Ip' | tail -1)"
        if [ -z "$s" ]; then
            s="$(tr -d '\r' < "$out.headers" 2>/dev/null | sed -n 's/^x-ratelimit-reset:[[:space:]]*\([0-9]\+\).*/\1/Ip' | tail -1)"
            [ -z "$s" ] || s=$((s - $(date +%s)))
        fi
        WAIT_MIN=""
        if [ -n "$s" ] && [ "$s" -gt 0 ]; then WAIT_MIN=$(((s + 59) / 60)); fi
    fi
    return 1
}

fetch_reason() {  # fetch_reason fw|bep: the sentence for FETCH_ERR
    case "$FETCH_ERR" in
        offline) t e_offline ;;
        server) t e_server ;;
        limit) if [ -n "$WAIT_MIN" ]; then t e_limit; else t e_limit_later; fi ;;
        notfound) if [ "$1" = fw ]; then t e_notfound_fw; else t e_notfound; fi ;;
        tls) t e_tls ;;
        toobig) t e_toobig ;;
        *) t e_other ;;
    esac
}

# 0 when the file has the given size (when one is given, else at most 20 MB)
# and SHA-256; BAD_WHAT names what differed.
check_file() {  # check_file <file> <size or ""> <sha256>
    local size actual
    size="$(wc -c < "$1" | tr -d ' ')"
    if [ -n "$2" ] && [ "$size" != "$2" ]; then
        BAD_WHAT="size"; log "size $size bytes, expected $2"; return 1
    fi
    if [ "$size" -gt "$MAX_DOWNLOAD" ]; then
        BAD_WHAT="size"; log "size $size bytes, over $MAX_DOWNLOAD"; return 1
    fi
    actual="$(sha256sum "$1" | cut -d' ' -f1)"
    if [ "$actual" != "$3" ]; then
        BAD_WHAT="SHA-256"; log "SHA-256 mismatch: expected $3, actual $actual, size $size bytes"; return 1
    fi
    log "SHA-256 OK (${actual:0:8}...${actual: -5}), $size bytes"
}

# Unpacks only the named plugin folders and the Preloader from a ModFramework
# zip into the staging folder. An entry whose path would leave the folder is
# skipped; a folder the zip does not have is an error (exit 5).
unpack_framework() {  # unpack_framework <zip> <folder> <plugin folder names...>
    python3 - "$@" <<'PY'
import os, shutil, sys, zipfile
src, dst, names = sys.argv[1], sys.argv[2], sys.argv[3:]
patcher = "BepInEx/patchers/DragNWash.ModFramework.Preloader.dll"
wanted = [f"BepInEx/plugins/{n}/" for n in names]
root = os.path.realpath(dst)
found = set()
with zipfile.ZipFile(src) as z:
    for info in z.infolist():
        name = info.filename
        if info.is_dir() or (name != patcher and not any(name.startswith(w) for w in wanted)):
            continue
        parts = name.split("/")
        target = os.path.realpath(os.path.join(root, *parts))
        if "\\" in name or ":" in name or any(p in ("", ".", "..") for p in parts) \
                or not target.startswith(root + os.sep):
            print(f"skipped {name!r}: outside the folder", file=sys.stderr)
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with z.open(info) as r, open(target, "wb") as w:
            shutil.copyfileobj(r, w)
        found.add(name if name == patcher else "/".join(parts[:3]) + "/")
missing = [w.split("/")[2] for w in wanted if w not in found] + (["Preloader"] if patcher not in found else [])
if missing:
    print("not in the zip: " + ", ".join(missing), file=sys.stderr)
    sys.exit(5)
PY
}

# -------------------------------------------------------------- framework --
# What the mod needs of ModFramework, against what the game folder has: the
# core, the Preloader and the libraries named in "needs", nothing else. The
# release is fetched when the core is missing or older than the release the
# mod names, or when the Preloader or a library is missing or below its
# minimum. Otherwise nothing is fetched and nothing connects. FW_MEETS says
# whether what is there meets every minimum anyway, so a failed download can
# still leave the choice of installing only the mod.
FW_BUNDLED=0 FW_FETCH=0 FW_MEETS=1
FW_PARTS=()
plan_framework() {
    local name have min verdict
    FW_HAVE="$(dll_version "$GAME_DIR/BepInEx/plugins/$FRAMEWORK_PREFIX/$FRAMEWORK_PREFIX.dll")"
    FW_PARTS=("$FRAMEWORK_PREFIX")
    for name in "${NEED_ORDER[@]}"; do
        if [ "$name" != "$FRAMEWORK_PREFIX" ]; then FW_PARTS+=("$name"); fi
    done
    for name in "${FW_PARTS[@]}"; do
        have="$(dll_version "$GAME_DIR/BepInEx/plugins/$name/$name.dll")"
        min="${NEEDS[$name]:-}"
        if [ -z "$have" ]; then
            verdict="missing" FW_FETCH=1 FW_MEETS=0
        elif [ -n "$min" ] && [ "$(ver_cmp "$have" "$min")" = -1 ]; then
            verdict="below the minimum" FW_FETCH=1 FW_MEETS=0
        elif [ "$name" = "$FRAMEWORK_PREFIX" ] && [ "$(ver_cmp "$have" "$FW_VERSION")" = -1 ]; then
            verdict="update to $FW_VERSION" FW_FETCH=1
        else
            verdict="keep"
        fi
        log "$name: have ${have:-none}${min:+, needs >= $min} -> $verdict"
    done
    if [ ! -f "$GAME_DIR/BepInEx/patchers/$FRAMEWORK_PATCHER" ]; then
        FW_FETCH=1 FW_MEETS=0
        log "Preloader: missing"
    fi
    if [ "$FW_FETCH" -eq 1 ]; then
        log "ModFramework $FW_VERSION: needed"
    else
        log "ModFramework $FW_VERSION: not needed, nothing is downloaded"
    fi
}

# Before the first connection: where to, why, what is sent and how the file is
# checked, the same points as Install.exe's window. Download, use zips
# downloaded by hand (then nothing connects), or cancel. --yes counts as
# agreeing, as it did for BepInEx.
consent() {
    local why="" pick text
    C_REPOS="" C_FILES=""
    if [ "$NET_FW" -eq 1 ]; then
        if [ -z "$FW_HAVE" ]; then
            why="$(t c_fw)
$(t c_fw_none)"
        elif [ "$(ver_cmp "$FW_HAVE" "$FW_VERSION")" = -1 ]; then
            why="$(t c_fw)
$(t c_fw_have)"
        else
            why="$(t c_fw)
$(t c_fw_part)"
        fi
        C_REPOS="$FRAMEWORK_REPO"
        C_FILES="DragNWash.ModFramework-$FW_VERSION.zip ($(((FW_SIZE + 512) / 1024)) KB)"
    fi
    if [ "$NET_BEP" -eq 1 ]; then
        why="${why:+$why
}$(t c_bep)"
        C_REPOS="${C_REPOS:+$C_REPOS, }BepInEx/BepInEx"
        C_FILES="${C_FILES:+$C_FILES, }${BEPINEX_URL##*/}"
    fi
    text="$(t confirm_install)
$GAME_DIR

$(t c_head)
$why

$(t c_to)
  $(t c_gh)
  $(t c_ra)
$(t c_what)
$(t c_sent)
$(t c_check)
$(t c_noapi)"
    if [ "$NET_FW" -eq 1 ]; then text="$text
$(t c_page) $FW_PAGE"; fi
    log "asking before connecting: $C_FILES from github.com ($C_REPOS), User-Agent $USER_AGENT"
    if [ "$ASSUME_YES" -eq 1 ]; then
        log "consent: download (--yes)"
        return 0
    fi
    if [ "$GUI" -eq 1 ]; then
        pick=0
        kdialog --title "$TITLE" --yes-label "$(t c_download)" --no-label "$(t c_zip)" \
            --cancel-label "$(t c_cancel)" --yesnocancel "$text" >/dev/null 2>&1 || pick=$?
        case "$pick" in 0) pick=download ;; 1) pick=zip ;; *) pick=cancel ;; esac
    else
        echo "$text"
        echo
        echo "  1) $(t c_download)"
        echo "  2) $(t c_zip)"
        echo "  3) $(t c_cancel)"
        read -r -p "> " pick || pick=""
        case "$pick" in 1|"") pick=download ;; 2) pick=zip ;; *) pick=cancel ;; esac
    fi
    log "consent: $pick"
    case "$pick" in
        download) return 0 ;;
        cancel) exit 1 ;;
    esac
    if [ "$NET_FW" -eq 1 ]; then
        FRAMEWORK_ZIP="$(ask_file "$(t zip_fw)")" || exit 1
        NET_FW=0
    fi
    if [ "$NET_BEP" -eq 1 ]; then
        LOCAL_ZIP="$(ask_file "$(t zip_bep)")" || exit 1
        NET_BEP=0
    fi
}

ask_file() {  # ask_file <label>: a zip the player picks
    local f=""
    if [ "$GUI" -eq 1 ]; then
        f="$(kdialog --title "$1" --getopenfilename "$HOME" "*.zip" 2>/dev/null)" || return 1
    else
        read -r -p "$1 " f || return 1
        f="${f/#\~/$HOME}"
    fi
    [ -n "$f" ] || return 1
    echo "$f"
}

# One ModFramework folder from <root>/BepInEx/plugins, over what is there,
# unless the installed copy is newer (or, with "same", the same version too).
# Only the files the new copy has are written; others in the folder stay.
install_part() {  # install_part <root> <folder> newer|same
    local src="$1/BepInEx/plugins/$2" have offered c
    have="$(dll_version "$GAME_DIR/BepInEx/plugins/$2/$2.dll")"
    offered="$(dll_version "$src/$2.dll")"
    if [ -n "$have" ] && [ -n "$offered" ]; then
        c="$(ver_cmp "$have" "$offered")"
        if [ "$c" = 1 ]; then say "$2: kept $have (newer than $offered)"; return 0; fi
        if [ "$c" = 0 ] && [ "$3" = same ]; then say "$2: kept $have (same)"; return 0; fi
    fi
    tx_copy_tree "$src" "BepInEx/plugins/$2" || fail "$(t copy_failed)"
    enable_folder "$2"
    say "$2: ${offered:-ok}"
}

install_preloader() {  # install_preloader <root> newer|same
    local rel="BepInEx/patchers/$FRAMEWORK_PATCHER" have offered c
    have="$(dll_version "$GAME_DIR/$rel")"
    offered="$(dll_version "$1/$rel")"
    if [ -n "$have" ] && [ -n "$offered" ]; then
        c="$(ver_cmp "$have" "$offered")"
        if [ "$c" = 1 ] || { [ "$c" = 0 ] && [ "$2" = same ]; }; then
            log "Preloader: kept $have (offered $offered)"
            return 0
        fi
    fi
    tx_copy "$1/$rel" "$rel" || { BAD_PART="$rel"; fail "$(t copy_failed)"; }
    log "Preloader: ${offered:-ok}"
}

other_mods() {
    local entry name p skip
    for entry in "$GAME_DIR/BepInEx/plugins"/*; do
        [ -e "$entry" ] || continue
        name="$(basename "$entry")"
        case "$name" in "$FRAMEWORK_PREFIX"*) continue ;; esac
        skip=0
        for p in "${PLUGINS[@]}"; do [ "$name" = "$p" ] && skip=1; done
        [ "$skip" -eq 1 ] || echo "$name"
    done
}

if [ "$MODE" = install ]; then
    # The mod's choices
    # The ids are read first: a prompt inside a loop fed by mf would read
    # its answer from mf instead of the keyboard.
    declare -A VALUE=()
    mapfile -t choice_ids < <(mf choices)
    for id in "${choice_ids[@]}"; do
        [ -n "$id" ] || continue
        if [ -n "${CHOICE[$id]:-}" ]; then
            mf has "$id" "${CHOICE[$id]}" || fail "Unknown value for $id: ${CHOICE[$id]}"
            VALUE[$id]="${CHOICE[$id]}"
            continue
        fi
        default="$(mf default "$id" "$GAME_DIR" "$UI_LOCALE")"
        label="$(mf label "$id" "$UI")"
        if [ "$ASSUME_YES" -eq 1 ]; then
            VALUE[$id]="$default"
        elif [ "$GUI" -eq 1 ]; then
            args=()
            while IFS=$'\t' read -r value name; do
                state=off; [ "$value" = "$default" ] && state=on
                args+=("$value" "$name" "$state")
            done < <(mf options "$id")
            VALUE[$id]="$(kdialog --title "$TITLE" --radiolist "$label" "${args[@]}")" || exit 1
        else
            say "$label:"
            mapfile -t rows < <(mf options "$id")
            for i in "${!rows[@]}"; do
                value="${rows[$i]%%$'\t'*}"; name="${rows[$i]#*$'\t'}"
                mark=" "; [ "$value" = "$default" ] && mark="*"
                printf ' %s %2d) %s (%s)\n' "$mark" "$((i + 1))" "$name" "$value"
            done
            read -r -p "> " pick || pick=""
            if [ -z "$pick" ]; then
                VALUE[$id]="$default"
            elif [[ "$pick" =~ ^[0-9]+$ ]] && [ "$pick" -ge 1 ] && [ "$pick" -le "${#rows[@]}" ]; then
                VALUE[$id]="${rows[$((pick - 1))]%%$'\t'*}"
            else
                mf has "$id" "$pick" || fail "Unknown value for $id: $pick"
                VALUE[$id]="$pick"
            fi
        fi
    done

    # What has to come from where. Nothing in the game folder changes until
    # every download is in and checked.
    BEP_FETCH=0
    if [ ! -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then BEP_FETCH=1; fi
    for src in "$HERE/BepInEx/plugins/$FRAMEWORK_PREFIX"*/; do
        if [ -d "$src" ]; then FW_BUNDLED=1; fi
    done
    if [ "$FW_BUNDLED" -eq 1 ]; then
        log "ModFramework: the mod's zip carries it; that copy is used"
    elif [ -n "$FW_VERSION" ]; then
        plan_framework
    fi
    if [ -n "$FRAMEWORK_ZIP" ] && [ "$FW_FETCH" -eq 0 ]; then
        log "--framework-zip not used: nothing of ModFramework has to be installed"
    fi
    NET_BEP=0 NET_FW=0
    if [ "$BEP_FETCH" -eq 1 ] && [ -z "$LOCAL_ZIP" ]; then
        [ "$NO_DOWNLOAD" -eq 0 ] || fail "$(t nodl_bep)"
        NET_BEP=1
    fi
    if [ "$FW_FETCH" -eq 1 ] && [ -z "$FRAMEWORK_ZIP" ] && [ "$NO_DOWNLOAD" -eq 0 ]; then NET_FW=1; fi

    if [ "$NET_BEP" -eq 1 ] || [ "$NET_FW" -eq 1 ]; then
        consent
    else
        ask_yes "$(t confirm_install)
$GAME_DIR" 1 || exit 1
    fi

    # Downloads go to a folder only this user can read, and are checked there.
    WORK="$(mktemp -d)"
    BEP_FILE=""
    if [ "$BEP_FETCH" -eq 1 ]; then
        BEP_FILE="$WORK/bepinex.zip"
        if [ -n "$LOCAL_ZIP" ]; then
            ZIP_PATH="$LOCAL_ZIP"
            [ -f "$LOCAL_ZIP" ] || fail "$(t zip_missing)"
            cp -f "$LOCAL_ZIP" "$BEP_FILE"
        else
            say "$(t bep_get)"
            fetch "$BEPINEX_URL" "$BEP_FILE" || fail "$(t bep_fail) $(fetch_reason bep)
$(t unchanged)"
        fi
        check_file "$BEP_FILE" "" "$BEPINEX_SHA256" || fail "$(t bep_bad)
$(t unchanged)"
    fi

    FW_FILE=""
    if [ "$FW_FETCH" -eq 1 ]; then
        FW_FILE="$WORK/framework.zip"
        fw_msg=""
        if [ -n "$FRAMEWORK_ZIP" ]; then
            ZIP_PATH="$FRAMEWORK_ZIP"
            log "ModFramework: using $FRAMEWORK_ZIP"
            if [ ! -f "$FRAMEWORK_ZIP" ]; then
                fw_msg="$(t zip_missing)"
            elif [ "$(wc -c < "$FRAMEWORK_ZIP" | tr -d ' ')" != "$FW_SIZE" ]; then
                BAD_WHAT="size"
                log "size $(wc -c < "$FRAMEWORK_ZIP" | tr -d ' ') bytes, expected $FW_SIZE"
                fw_msg="$(t fw_bad_zip)"
            else
                cp -f "$FRAMEWORK_ZIP" "$FW_FILE"
                check_file "$FW_FILE" "$FW_SIZE" "$FW_SHA256" || fw_msg="$(t fw_bad_zip)"
            fi
        elif [ "$NO_DOWNLOAD" -eq 1 ]; then
            fw_msg="$(t nodl_fw)"
        else
            say "$(t fw_get)"
            log "ModFramework: downloading $FW_URL"
            if ! fetch "$FW_URL" "$FW_FILE"; then
                fw_msg="$(fetch_reason fw)"
            elif ! check_file "$FW_FILE" "$FW_SIZE" "$FW_SHA256"; then
                fw_msg="$(t fw_bad_dl)"
            fi
        fi
        if [ -n "$fw_msg" ]; then
            rm -f "$FW_FILE"
            FW_FILE=""
            fw_msg="$fw_msg
$(t unchanged)"
            # No way around a failed secure connection is offered.
            if [ "$FETCH_ERR" != tls ]; then
                fw_msg="$fw_msg

$(t fw_manual)
$FW_PAGE"
            fi
            if [ "$FW_MEETS" -eq 1 ] && ask_yes "$fw_msg

$(t fw_keep_ask)" 1; then
                warn "$(t fw_kept_old)"
            else
                fail "$fw_msg"
            fi
        else
            say "$(t fw_ok)"
        fi
    fi

    # From here on every change is backed up and written down, and put back
    # if anything fails.
    tx_begin

    # ModFramework: only the folders this mod needs, out of the checked zip.
    if [ -n "$FW_FILE" ]; then
        unpack_rc=0
        unpack_framework "$FW_FILE" "$STAGE/framework" "${FW_PARTS[@]}" 2> "$WORK/unpack.log" || unpack_rc=$?
        if [ -s "$WORK/unpack.log" ]; then log "unpack: $(tr '\n' ' ' < "$WORK/unpack.log")"; fi
        if [ "$unpack_rc" -eq 5 ]; then
            BAD_PART="$(sed -n 's/^not in the zip: //p' "$WORK/unpack.log")"
            fail "$(t fw_short)"
        elif [ "$unpack_rc" -ne 0 ]; then
            BAD_PART="DragNWash.ModFramework-$FW_VERSION.zip"
            fail "$(t unpack_failed)"
        fi
        for name in "${FW_PARTS[@]}"; do
            offered="$(dll_version "$STAGE/framework/BepInEx/plugins/$name/$name.dll")"
            min="${NEEDS[$name]:-}"
            if [ -z "$offered" ] || { [ -n "$min" ] && [ "$(ver_cmp "$offered" "$min")" = -1 ]; }; then
                BAD_PART="$name${min:+ $min}"
                fail "$(t fw_short)"
            fi
        done
    fi

    # BepInEx
    if [ -n "$BEP_FILE" ]; then
        unzip -q "$BEP_FILE" -d "$STAGE/bepinex" || { BAD_PART="${BEPINEX_URL##*/}"; fail "$(t unpack_failed)"; }
        tx_copy_tree "$STAGE/bepinex" "" || fail "$(t copy_failed)"
        tx_save "BepInEx/$MARKER" || { BAD_PART="BepInEx/$MARKER"; fail "$(t copy_failed)"; }
        echo "BepInEx was added by the Drag'n Wash mod installer." > "$GAME_DIR/BepInEx/$MARKER"
        say "$(t bep_ok)"
    else
        say "$(t bep_have)"
    fi

    # run_bepinex.sh
    if [ -f "$GAME_DIR/run_bepinex.sh" ]; then
        if ! grep -qx "executable_name=\"$GAME_BIN\"" "$GAME_DIR/run_bepinex.sh"; then
            tx_save run_bepinex.sh || { BAD_PART=run_bepinex.sh; fail "$(t copy_failed)"; }
            sed -i 's/^executable_name=.*/executable_name="'"$GAME_BIN"'"/' "$GAME_DIR/run_bepinex.sh"
        fi
        chmod +x "$GAME_DIR/run_bepinex.sh"
        say "run_bepinex.sh: executable_name=\"$GAME_BIN\""
    fi

    # Drag'n Wash ModFramework and its libraries, each in its own folder;
    # never replaced by an older copy. A copy the mod's zip carries goes over
    # the same version, as before; one from a release zip leaves it alone.
    if [ "$FW_BUNDLED" -eq 1 ]; then
        for src in "$HERE/BepInEx/plugins/$FRAMEWORK_PREFIX"*/; do
            if [ -d "$src" ]; then install_part "$HERE" "$(basename "$src")" newer; fi
        done
        if [ -f "$HERE/BepInEx/patchers/$FRAMEWORK_PATCHER" ]; then install_preloader "$HERE" newer; fi
    elif [ -n "$FW_FILE" ]; then
        for name in "${FW_PARTS[@]}"; do install_part "$STAGE/framework" "$name" same; done
        install_preloader "$STAGE/framework" same
    elif [ -n "$FW_VERSION" ] && [ "$FW_FETCH" -eq 0 ]; then
        say "$(t fw_have)"
    fi

    # The mod, copied over what is there: files the player added are kept.
    for p in "${PLUGINS[@]}"; do
        tx_copy_tree "$HERE/BepInEx/plugins/$p" "BepInEx/plugins/$p" || fail "$(t copy_failed)"
        enable_folder "$p"
        say "$p: $(t mod_ok)"
    done
    # The manifest copy and the choices are written in the download folder
    # first, so a file that comes out the same is not touched.
    rel="BepInEx/plugins/${PLUGINS[0]}/mod-install.json"
    BAD_PART="$rel"
    mf copy "$WORK/mod-install.json" && tx_copy "$WORK/mod-install.json" "$rel" || fail "$(t copy_failed)"

    cfgs=()
    for id in "${!VALUE[@]}"; do
        rel="BepInEx/config/$(mf cfgfile "$id")"
        BAD_PART="$rel"
        if [ ! -f "$WORK/cfg/$rel" ]; then
            mkdir -p "$WORK/cfg/BepInEx/config"
            if [ -f "$GAME_DIR/$rel" ]; then cp -f "$GAME_DIR/$rel" "$WORK/cfg/$rel"; fi
            cfgs+=("$rel")
        fi
        mf apply "$id" "$WORK/cfg" "${VALUE[$id]}" || fail "$(t copy_failed)"
        say "$id: ${VALUE[$id]}"
    done
    for rel in "${cfgs[@]}"; do
        BAD_PART="$rel"
        tx_copy "$WORK/cfg/$rel" "$rel" || fail "$(t copy_failed)"
    done

    tx_commit
    if [ "$TX_REPLACED" -gt 0 ]; then say "$(t backup_done)"; fi

    # Steam launch option
    if [ "$LAUNCH_OPTIONS" -eq 0 ]; then
        log "launch options left alone (--no-launch-option)"
    elif [ "$(launch_options_state)" = yes ]; then
        say "$(t lo_same)"
    else
        rc=0; with_steam_closed set || rc=$?
        case "$rc" in
            0) say "$(t lo_done) $LAUNCH_OPTION" ;;
            3) warn "$(t steam_slow)
$(t lo_manual):
  $LAUNCH_OPTION" ;;
            *) warn "$(t lo_manual):
  $LAUNCH_OPTION" ;;
        esac
    fi

    finish_message "$(t done)"
else
    any=0
    for p in "${PLUGINS[@]}"; do [ -d "$GAME_DIR/BepInEx/plugins/$p" ] && any=1; done
    if [ "$any" -eq 0 ]; then
        finish_message "$(t nothing)"
        exit 0
    fi
    ask_yes "$(t confirm_uninstall)
$GAME_DIR" 1 || exit 1

    keep=1
    if [ "$REMOVE_DATA" -eq 1 ]; then
        keep=0
    elif [ "$ASSUME_YES" -eq 0 ]; then
        has_data=0
        while IFS= read -r k; do [ -e "$GAME_DIR/BepInEx/plugins/$k" ] && has_data=1; done < <(mf keep)
        [ -d "$GAME_DIR/BepInEx/SaveHistory" ] && has_data=1
        if [ "$has_data" -eq 1 ]; then ask_yes "$(t keep)" 1 || keep=0; fi
    fi

    for p in "${PLUGINS[@]}"; do
        forget_folder "$p"
        if [ "$keep" -eq 1 ]; then
            kept="$(mf prune "$GAME_DIR" "$p")"
        else
            rm -rf "$GAME_DIR/BepInEx/plugins/$p"; kept=""
        fi
        say "$p: $(t mod_removed)${kept:+ ($kept)}"
    done
    while IFS= read -r f; do
        [ -n "$f" ] && rm -f "$GAME_DIR/BepInEx/config/$f"
    done < <(mf configFiles)
    # The installer's staging folder and the backup of the last install.
    rm -rf "${GAME_DIR:?}/$INST_REL"

    # The framework stays while any other mod is installed.
    if [ -n "$(other_mods)" ]; then
        say "$(t fw_kept)"
    else
        if [ -n "$(find "$GAME_DIR/BepInEx/plugins" -mindepth 1 -maxdepth 1 -name "$FRAMEWORK_PREFIX*" 2>/dev/null | head -1)" ]; then
            find "$GAME_DIR/BepInEx/plugins" -mindepth 1 -maxdepth 1 -name "$FRAMEWORK_PREFIX*" -exec rm -rf {} +
            rm -f "$GAME_DIR/BepInEx/patchers/$FRAMEWORK_PATCHER" "$GAME_DIR/BepInEx/config/com.tomxv.dragnwash.modframework"*
            say "$(t fw_removed)"
        fi
        # Save snapshots taken by the framework's saves library.
        if [ -d "$GAME_DIR/BepInEx/SaveHistory" ] && [ "$keep" -eq 0 ]; then
            rm -rf "$GAME_DIR/BepInEx/SaveHistory"
        fi
    fi

    # BepInEx and the launch option only matter to other mods now. If there
    # are none, offer to remove BepInEx (the default follows whether an
    # installer added it), and take ./run_bepinex.sh out of the launch options
    # either way: without BepInEx's files it would stop the game from starting.
    if [ -n "$(other_mods)" ] || [ -n "$(find "$GAME_DIR/BepInEx/patchers" -mindepth 1 2>/dev/null | head -1)" ]; then
        say "$(t bep_kept)"
    else
        if [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then
            default_remove=0
            { [ -f "$GAME_DIR/BepInEx/$MARKER" ] || [ -f "$GAME_DIR/BepInEx/$OLD_MARKER" ]; } && default_remove=1
            remove_bep=0
            if [ "$REMOVE_BEPINEX" -eq 1 ]; then
                remove_bep=1
            elif [ "$ASSUME_YES" -eq 0 ] && ask_yes "$(t rmbep)" "$default_remove"; then
                remove_bep=1
            fi
            if [ "$remove_bep" -eq 1 ]; then
                rm -f "$GAME_DIR/run_bepinex.sh" "$GAME_DIR/libdoorstop.so" "$GAME_DIR/.doorstop_version"
                if [ -f "$GAME_DIR/changelog.txt" ] && grep -qi 'bepinex\|doorstop' "$GAME_DIR/changelog.txt"; then rm -f "$GAME_DIR/changelog.txt"; fi
                if [ "$keep" -eq 1 ] && [ -n "$(find "$GAME_DIR/BepInEx/plugins" "$GAME_DIR/BepInEx/SaveHistory" -mindepth 1 -maxdepth 1 2>/dev/null | head -1)" ]; then
                    find "$GAME_DIR/BepInEx" -mindepth 1 -maxdepth 1 ! -name plugins ! -name SaveHistory -exec rm -rf {} +
                else
                    rm -rf "$GAME_DIR/BepInEx"
                fi
                say "$(t bep_removed)"
            fi
        fi
        if [ "$LAUNCH_OPTIONS" -eq 0 ]; then
            log "launch options left alone (--no-launch-option)"
        elif [ "$(launch_options_any)" = no ]; then
            log "no launch option to take out"
        else
            rc=0; with_steam_closed remove || rc=$?
            case "$rc" in
                0) say "$(t lo_removed)" ;;
                3) warn "$(t steam_slow_remove)
$(t lo_manual_remove)" ;;
                *) warn "$(t lo_manual_remove)" ;;
            esac
        fi
    fi
    finish_message "$(t undone)"
fi
