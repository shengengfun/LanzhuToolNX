//! 工具的在线下载 / 离线包导入导出。
//!
//! 地址来源：项目自带的 `update_tools.ps1` / `TOOLS_UPDATE_GUIDE.md`，
//! **并且逐个用 HTTP HEAD 实测过**。
//!
//! ⚠️ 实测发现原脚本里两个地址**已经失效（404）**：
//!   - `github.com/gpac/gpac/releases/download/v2.5-DEV/...`（GPAC 新版 release 已不再挂二进制）
//!   - `github.com/mbunkus/mkvtoolnix/releases/download/release-82.0/...`（tag 命名变了）
//! 所以这里换成了当前真实可用的地址，见下面的 `PACKAGES`。
//!
//! 两个设计要点：
//!
//! 1. **镜像自动降级**。GitHub 直连在国内经常断，所以每个 GitHub URL 都会先套一遍
//!    国内加速前缀（可配置、可留空），全部失败才回落直连。
//!    非 GitHub 的源（如 mkvtoolnix.download）直接连。
//!
//! 2. **不做"拉平"处理**。`tools::find()` 是按文件名递归索引的，
//!    所以压缩包里的目录结构可以原样保留，直接解到 `tools/<分类>/` 即可 ——
//!    既不用管各家的顶层目录叫什么，也不会互相覆盖。

use serde::{Deserialize, Serialize};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter, Manager};

/* ------------------------------------------------------------------ *
 * 清单
 * ------------------------------------------------------------------ */

#[derive(Clone, Copy)]
pub struct Source {
    pub url: &'static str,
    pub seven_z: bool,
}

#[derive(Clone, Copy)]
pub struct Pkg {
    pub id: &'static str,
    pub name: &'static str,
    pub desc: &'static str,
    pub required: bool,
    pub approx_mb: u32,
    /// 用来判断"装没装"：这些 exe 在 dest 下能找到就算已装
    pub provides: &'static [&'static str],
    /// 依次尝试；GitHub 的 URL 会先套镜像再直连
    pub sources: &'static [Source],
    /// 相对 tools/ 的目标目录
    pub dest: &'static str,
}

const fn s3(url: &'static str) -> Source {
    Source { url, seven_z: false }
}
const fn s7(url: &'static str) -> Source {
    Source { url, seven_z: true }
}

pub const PACKAGES: &[Pkg] = &[
    Pkg {
        id: "ffmpeg",
        name: "FFmpeg",
        desc: "压制 / 封装 / 抽取 / 探测的核心，必装",
        required: true,
        approx_mb: 185,
        provides: &["ffmpeg.exe", "ffprobe.exe"],
        sources: &[
            // BtbN 的静态 GPL 构建：自带全部编码器，不依赖旁挂 Dll
            s3("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"),
            // 老版本用的就是 gyan.dev 的 full build，作为备用源
            s7("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full.7z"),
        ],
        dest: "video/ffmpeg",
    },
    Pkg {
        id: "mkvtoolnix",
        name: "MKVToolNix",
        desc: "mkvmerge / mkvextract / mkvinfo，封装与抽取 mkv 必需",
        required: true,
        approx_mb: 40,
        provides: &["mkvmerge.exe", "mkvextract.exe"],
        sources: &[
            // 官方专用下载域名，非 GitHub，国内速度尚可，不需要套镜像
            s7("https://mkvtoolnix.download/windows/releases/92.0/mkvtoolnix-64-bit-92.0.7z"),
        ],
        dest: "video/mkvtoolnix",
    },
    Pkg {
        id: "qaac",
        name: "QAAC + refalac",
        desc: "AAC / ALAC 编码（需要系统已装 Apple 应用支持组件）",
        required: false,
        approx_mb: 7,
        provides: &["qaac.exe"],
        sources: &[s3(
            "https://github.com/nu774/qaac/releases/download/v3.07/qaac_3.07.zip",
        )],
        dest: "audio/encoders",
    },
    Pkg {
        id: "flac",
        name: "FLAC",
        desc: "无损音频编码，可选",
        required: false,
        approx_mb: 2,
        provides: &["flac.exe"],
        sources: &[s3(
            "https://github.com/xiph/flac/releases/download/1.5.0/flac-1.5.0-win.zip",
        )],
        dest: "audio/encoders",
    },
    Pkg {
        id: "gpac",
        name: "MP4Box (GPAC)",
        desc: "mp4 封装工具。本程序不依赖它（封装走 ffmpeg），仅在你想手工调用时需要",
        required: false,
        approx_mb: 30,
        provides: &["MP4Box.exe"],
        // GPAC 新版的 GitHub release 已经不再挂 Windows 二进制，没有稳定直链
        sources: &[],
        dest: "video/gpac",
    },
    Pkg {
        id: "fdkaac",
        name: "FDK-AAC",
        desc: "另一种 AAC 编码器。上游仓库没有发布二进制，需离线导入",
        required: false,
        approx_mb: 1,
        provides: &["fdkaac.exe"],
        sources: &[],
        dest: "audio/encoders",
    },
    Pkg {
        id: "nero",
        name: "Nero AAC Codec",
        desc: "老牌 AAC 编码器。Nero 官网要走下载表单，需离线导入",
        required: false,
        approx_mb: 2,
        provides: &["neroAacEnc.exe"],
        sources: &[],
        dest: "audio/encoders",
    },
];

/// 默认的国内加速前缀。留一个空串在最后，表示回落直连。
pub const DEFAULT_MIRRORS: &[&str] = &[
    "https://ghfast.top/",
    "https://gh-proxy.com/",
    "https://ghproxy.net/",
    "https://gh.llkk.cc/",
    "",
];

/* ------------------------------------------------------------------ *
 * 状态
 * ------------------------------------------------------------------ */

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PkgStatus {
    pub id: String,
    pub name: String,
    pub desc: String,
    pub required: bool,
    pub approx_mb: u32,
    pub installed: bool,
    pub missing: Vec<String>,
    pub dest: String,
    pub downloadable: bool,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Progress {
    pub pkg: String,
    pub phase: String,
    pub message: String,
    pub got: u64,
    pub total: u64,
    pub percent: f64,
    pub speed_kbps: f64,
}

#[derive(Default)]
pub struct FetchState {
    cancel: Mutex<Option<std::sync::Arc<AtomicBool>>>,
}

/* ------------------------------------------------------------------ *
 * 扫描：判断哪些已装
 * ------------------------------------------------------------------ */

fn contains_file(root: &Path, name: &str, depth: usize, budget: &mut u32) -> bool {
    if depth > 5 || *budget == 0 {
        return false;
    }
    let Ok(rd) = std::fs::read_dir(root) else { return false };
    for e in rd.flatten() {
        *budget -= 1;
        if *budget == 0 {
            return false;
        }
        let p = e.path();
        if p.is_dir() {
            if contains_file(&p, name, depth + 1, budget) {
                return true;
            }
        } else if p.file_name().and_then(|s| s.to_str()).map(|s| s.eq_ignore_ascii_case(name)) == Some(true) {
            return true;
        }
    }
    false
}

pub fn status(tools_dir: &str) -> Vec<PkgStatus> {
    let root = Path::new(tools_dir);
    PACKAGES
        .iter()
        .map(|p| {
            let dir = root.join(p.dest);
            let mut missing = Vec::new();
            for f in p.provides {
                // 先看目标分类目录，再看整个 tools（有些用户习惯平铺）
                let mut budget = 4000;
                let found = contains_file(&dir, f, 0, &mut budget)
                    || {
                        let mut b2 = 20000;
                        contains_file(root, f, 0, &mut b2)
                    };
                if !found {
                    missing.push((*f).to_string());
                }
            }
            PkgStatus {
                id: p.id.to_string(),
                name: p.name.to_string(),
                desc: p.desc.to_string(),
                required: p.required,
                approx_mb: p.approx_mb,
                installed: missing.is_empty(),
                missing,
                dest: dir.to_string_lossy().to_string(),
                downloadable: !p.sources.is_empty(),
            }
        })
        .collect()
}

/* ------------------------------------------------------------------ *
 * 下载
 * ------------------------------------------------------------------ */

fn candidates(pkg: &Pkg, mirrors: &[String]) -> Vec<(String, bool)> {
    let mut out = Vec::new();
    for src in pkg.sources {
        let is_github = src.url.contains("github.com");
        if is_github {
            for m in mirrors {
                if m.trim().is_empty() {
                    continue;
                }
                out.push((format!("{}{}", m.trim_end_matches('/'), format!("/{}", src.url)), src.seven_z));
            }
        }
        // 每个源最后都回落直连
        out.push((src.url.to_string(), src.seven_z));
    }
    out
}

fn emit(app: &AppHandle, p: Progress) {
    let _ = app.emit("tools://progress", p);
}

fn dl_one(
    app: &AppHandle,
    pkg: &Pkg,
    mirror_list: &[String],
    tmp: &Path,
    cancel: &AtomicBool,
) -> Result<(PathBuf, bool), String> {
    let list = candidates(pkg, mirror_list);
    if list.is_empty() {
        return Err(format!("{} 没有可用的下载源，请用离线包导入", pkg.name));
    }

    let mut last_err = String::from("未知错误");
    for (idx, (url, seven_z)) in list.iter().enumerate() {
        if cancel.load(Ordering::SeqCst) {
            return Err("已取消".into());
        }
        let host = url.split('/').nth(2).unwrap_or("?").to_string();
        emit(
            app,
            Progress {
                pkg: pkg.id.into(),
                phase: "fetch".into(),
                message: format!("尝试源 {}/{}：{}", idx + 1, list.len(), host),
                got: 0,
                total: 0,
                percent: 0.0,
                speed_kbps: 0.0,
            },
        );

        let resp = ureq::get(url)
            .timeout(Duration::from_secs(20))
            .call();
        let resp = match resp {
            Ok(r) => r,
            Err(e) => {
                last_err = format!("{} 连接失败：{}", host, e);
                continue;
            }
        };

        let total: u64 = resp
            .header("Content-Length")
            .and_then(|s| s.parse().ok())
            .unwrap_or(0);

        let ext = if *seven_z { "7z" } else { "zip" };
        let file_path = tmp.join(format!("{}.{}", pkg.id, ext));
        let mut out = match std::fs::File::create(&file_path) {
            Ok(f) => f,
            Err(e) => {
                last_err = format!("无法创建临时文件：{}", e);
                continue;
            }
        };

        let mut reader = resp.into_reader();
        let mut buf = vec![0u8; 256 * 1024];
        let mut got: u64 = 0;
        let start = Instant::now();
        let mut last_emit = Instant::now();
        let mut ok = true;

        loop {
            if cancel.load(Ordering::SeqCst) {
                return Err("已取消".into());
            }
            match reader.read(&mut buf) {
                Ok(0) => break,
                Ok(n) => {
                    if out.write_all(&buf[..n]).is_err() {
                        ok = false;
                        break;
                    }
                    got += n as u64;
                    if last_emit.elapsed() > Duration::from_millis(180) {
                        last_emit = Instant::now();
                        let secs = start.elapsed().as_secs_f64().max(0.001);
                        emit(
                            app,
                            Progress {
                                pkg: pkg.id.into(),
                                phase: "download".into(),
                                message: format!("来自 {}，已下载 {:.1} MB", host, got as f64 / 1048576.0),
                                got,
                                total,
                                percent: if total > 0 {
                                    got as f64 * 100.0 / total as f64
                                } else {
                                    0.0
                                },
                                speed_kbps: got as f64 / 1024.0 / secs,
                            },
                        );
                    }
                }
                Err(e) => {
                    last_err = format!("{} 传输中断：{}", host, e);
                    ok = false;
                    break;
                }
            }
        }
        drop(out);

        if !ok {
            let _ = std::fs::remove_file(&file_path);
            continue;
        }
        // 大小明显不对（比如拿到一个 HTML 错误页）就当失败
        if total > 0 && got < total / 2 {
            last_err = format!("{} 下载不完整（{}/{} 字节）", host, got, total);
            let _ = std::fs::remove_file(&file_path);
            continue;
        }

        return Ok((file_path, *seven_z));
    }

    Err(last_err)
}

fn extract(archive: &Path, dest: &Path, seven_z: bool) -> Result<(), String> {
    std::fs::create_dir_all(dest).map_err(|e| e.to_string())?;

    if seven_z {
        sevenz_rust::decompress_file(archive, dest)
            .map_err(|e| format!("7z 解压失败：{}", e))?;
        return Ok(());
    }

    let f = std::fs::File::open(archive).map_err(|e| e.to_string())?;
    let mut zip = zip::ZipArchive::new(f).map_err(|e| format!("zip 打开失败：{}", e))?;
    for i in 0..zip.len() {
        let mut entry = zip.by_index(i).map_err(|e| e.to_string())?;
        // enclosed_name 会挡掉 ../ 穿越
        let Some(rel) = entry.enclosed_name() else { continue };
        let out_path = dest.join(rel);
        if entry.is_dir() {
            std::fs::create_dir_all(&out_path).ok();
            continue;
        }
        if let Some(parent) = out_path.parent() {
            std::fs::create_dir_all(parent).ok();
        }
        let mut w = std::fs::File::create(&out_path).map_err(|e| e.to_string())?;
        std::io::copy(&mut entry, &mut w).map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// 后台线程执行下载。进度通过 `tools://progress` 事件推给前端。
pub fn download(app: AppHandle, ids: Vec<String>, tools_dir: String, mirrors: Vec<String>) -> Result<(), String> {
    {
        let st = app.state::<FetchState>();
        let mut g = st.cancel.lock().unwrap();
        if g.is_some() {
            return Err("已有下载任务在进行".into());
        }
        *g = Some(std::sync::Arc::new(AtomicBool::new(false)));
    }

    let list: Vec<&Pkg> = PACKAGES.iter().filter(|p| ids.iter().any(|i| i == p.id)).collect();
    if list.is_empty() {
        return Err("没有选中任何工具包".into());
    }

    let mirror_list: Vec<String> = if mirrors.is_empty() {
        DEFAULT_MIRRORS.iter().map(|s| s.to_string()).collect()
    } else {
        mirrors
    };

    std::thread::spawn(move || {
        let tmp = std::env::temp_dir().join("LanzhuTool").join("dl");
        let _ = std::fs::create_dir_all(&tmp);

        let cancel = {
            let st = app.state::<FetchState>();
            let g = st.cancel.lock().unwrap();
            g.as_ref().unwrap().clone()
        };

        let mut failed: Vec<String> = Vec::new();

        for pkg in list {
            emit(
                &app,
                Progress {
                    pkg: pkg.id.into(),
                    phase: "start".into(),
                    message: format!("开始获取 {}", pkg.name),
                    got: 0,
                    total: 0,
                    percent: 0.0,
                    speed_kbps: 0.0,
                },
            );

            match dl_one(&app, pkg, &mirror_list, &tmp, &cancel) {
                Ok((file, is7z)) => {
                    emit(
                        &app,
                        Progress {
                            pkg: pkg.id.into(),
                            phase: "extract".into(),
                            message: format!("解压到 tools/{}", pkg.dest),
                            got: 0,
                            total: 0,
                            percent: 100.0,
                            speed_kbps: 0.0,
                        },
                    );
                    let dest = Path::new(&tools_dir).join(pkg.dest);
                    match extract(&file, &dest, is7z) {
                        Ok(()) => {
                            emit(
                                &app,
                                Progress {
                                    pkg: pkg.id.into(),
                                    phase: "done".into(),
                                    message: format!("{} 安装完成", pkg.name),
                                    got: 0,
                                    total: 0,
                                    percent: 100.0,
                                    speed_kbps: 0.0,
                                },
                            );
                        }
                        Err(e) => {
                            failed.push(format!("{}：{}", pkg.name, e));
                            emit(
                                &app,
                                Progress {
                                    pkg: pkg.id.into(),
                                    phase: "error".into(),
                                    message: e,
                                    got: 0,
                                    total: 0,
                                    percent: 0.0,
                                    speed_kbps: 0.0,
                                },
                            );
                        }
                    }
                    let _ = std::fs::remove_file(&file);
                }
                Err(e) => {
                    if e == "已取消" {
                        emit(
                            &app,
                            Progress {
                                pkg: pkg.id.into(),
                                phase: "error".into(),
                                message: "已取消".into(),
                                got: 0,
                                total: 0,
                                percent: 0.0,
                                speed_kbps: 0.0,
                            },
                        );
                        break;
                    }
                    failed.push(format!("{}：{}", pkg.name, e));
                    emit(
                        &app,
                        Progress {
                            pkg: pkg.id.into(),
                            phase: "error".into(),
                            message: e,
                            got: 0,
                            total: 0,
                            percent: 0.0,
                            speed_kbps: 0.0,
                        },
                    );
                }
            }
        }

        {
            let st = app.state::<FetchState>();
            *st.cancel.lock().unwrap() = None;
        }

        let _ = app.emit(
            "tools://finished",
            serde_json::json!({ "failed": failed }),
        );
    });

    Ok(())
}

pub fn cancel(app: &AppHandle) {
    // 先取出 Arc 再解锁，避免临时 State 的生命周期问题
    let flag = {
        let st = app.state::<FetchState>();
        let g = st.cancel.lock().unwrap();
        g.as_ref().cloned()
    };
    if let Some(c) = flag {
        c.store(true, Ordering::SeqCst);
    }
}

/* ------------------------------------------------------------------ *
 * 离线包：导入 / 导出
 * ------------------------------------------------------------------ */

/// 把一个本地压缩包（zip / 7z）或文件夹合并进 tools/。
/// 用于「打包好的离线版」——适合没有工具、或者网络不通的机器。
pub fn import_offline(path: String, tools_dir: String) -> Result<String, String> {
    let src = PathBuf::from(&path);
    if !src.exists() {
        return Err(format!("路径不存在：{}", path));
    }
    let dest = PathBuf::from(&tools_dir);

    if src.is_dir() {
        copy_tree(&src, &dest)?;
        return Ok(format!("已从文件夹导入到 {}", dest.to_string_lossy()));
    }

    let ext = src
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    match ext.as_str() {
        "zip" => extract(&src, &dest, false)?,
        "7z" => extract(&src, &dest, true)?,
        _ => return Err("只支持 .zip / .7z 压缩包，或者直接选文件夹".into()),
    }
    Ok(format!("已从 {} 导入到 {}", src.to_string_lossy(), dest.to_string_lossy()))
}

fn copy_tree(from: &Path, to: &Path) -> Result<(), String> {
    std::fs::create_dir_all(to).map_err(|e| e.to_string())?;
    for e in std::fs::read_dir(from).map_err(|e| e.to_string())?.flatten() {
        let p = e.path();
        let target = to.join(e.file_name());
        if p.is_dir() {
            copy_tree(&p, &target)?;
        } else {
            if let Some(par) = target.parent() {
                std::fs::create_dir_all(par).ok();
            }
            std::fs::copy(&p, &target).map_err(|e| format!("复制 {} 失败：{}", p.display(), e))?;
        }
    }
    Ok(())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ExportOptions {
    pub output: String,
    pub tools_dir: String,
    /// 排除 ffplay.exe：它占 200MB+，而本程序从来不用它
    pub exclude_ffplay: bool,
}

pub fn export_offline(app: AppHandle, opt: ExportOptions) -> Result<(), String> {
    let root = PathBuf::from(&opt.tools_dir);
    if !root.is_dir() {
        return Err(format!("工具目录不存在：{}", opt.tools_dir));
    }

    std::thread::spawn(move || {
        let r = (|| -> Result<u64, String> {
            let f = std::fs::File::create(&opt.output).map_err(|e| format!("创建压缩包失败：{}", e))?;
            let mut zw = zip::ZipWriter::new(f);
            let opts: zip::write::FileOptions<()> =
                zip::write::FileOptions::default().compression_method(zip::CompressionMethod::Deflated);

            let mut count: u64 = 0;
            let mut stack = vec![root.clone()];
            while let Some(dir) = stack.pop() {
                for e in std::fs::read_dir(&dir).map_err(|e| e.to_string())?.flatten() {
                    let p = e.path();
                    if p.is_dir() {
                        stack.push(p);
                        continue;
                    }
                    let name = p.file_name().and_then(|s| s.to_str()).unwrap_or("");
                    if opt.exclude_ffplay && name.eq_ignore_ascii_case("ffplay.exe") {
                        continue;
                    }
                    let rel = p.strip_prefix(&root).map_err(|e| e.to_string())?;
                    let rel_str = rel.to_string_lossy().replace('\\', "/");
                    zw.start_file(rel_str, opts).map_err(|e| e.to_string())?;
                    let mut f = std::fs::File::open(&p).map_err(|e| e.to_string())?;
                    std::io::copy(&mut f, &mut zw).map_err(|e| e.to_string())?;
                    count += 1;

                    if count % 25 == 0 {
                        let _ = app.emit(
                            "tools://progress",
                            Progress {
                                pkg: "export".into(),
                                phase: "export".into(),
                                message: format!("已打包 {} 个文件", count),
                                got: count,
                                total: 0,
                                percent: 0.0,
                                speed_kbps: 0.0,
                            },
                        );
                    }
                }
            }
            zw.finish().map_err(|e| e.to_string())?;
            Ok(count)
        })();

        let msg = match r {
            Ok(n) => serde_json::json!({ "ok": true, "files": n }),
            Err(e) => serde_json::json!({ "ok": false, "error": e }),
        };
        let _ = app.emit("tools://exported", msg);
    });

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 扫描逻辑的实测：拿仓库里真实存在的 tools 目录跑一遍。
    /// 目录不存在时直接跳过，保证在别的机器上也能跑测试。
    #[test]
    fn detects_installed_packages_in_repo_tools() {
        let dir = r"D:\Project\LanzhuToolNX\tools";
        if !Path::new(dir).is_dir() {
            eprintln!("跳过：本机没有 {dir}");
            return;
        }

        let list = status(dir);
        assert_eq!(list.len(), PACKAGES.len());

        let get = |id: &str| list.iter().find(|p| p.id == id).expect("包不存在");

        // 这几个在仓库里确实存在，应该被识别为已安装
        for id in ["ffmpeg", "mkvtoolnix"] {
            let p = get(id);
            assert!(p.installed, "{} 应识别为已安装，但仍缺 {:?}", p.name, p.missing);
        }

        // 必装包必须标 required
        assert!(get("ffmpeg").required);
        assert!(get("mkvtoolnix").required);

        // 没有稳定直链的包必须标成"不可下载"，前端才不会给一个点了必失败的按钮
        for id in ["gpac", "fdkaac", "nero"] {
            assert!(!get(id).downloadable, "{id} 不该被标为可下载");
        }
        // 有直链的必须标可下载
        for id in ["ffmpeg", "mkvtoolnix", "qaac", "flac"] {
            assert!(get(id).downloadable, "{id} 应该可下载");
        }
    }

    /// 所有下载地址都必须是 https，且没有明显的占位符
    #[test]
    fn all_source_urls_are_sane() {
        for p in PACKAGES {
            for s in p.sources {
                assert!(s.url.starts_with("https://"), "{} 的地址不是 https: {}", p.id, s.url);
                assert!(!s.url.contains("XXX"), "{} 的地址还有占位符: {}", p.id, s.url);
                assert!(!s.url.contains("github.com/gpac/gpac/releases/download/v2.5-DEV"),
                    "这个地址实测已 404，别再放回来");
            }
        }
    }

    /// 镜像拼接必须产出 ghproxy 那种 `前缀/原始URL` 的形式
    #[test]
    fn mirror_prefix_format() {
        let pkg = PACKAGES.iter().find(|p| p.id == "qaac").unwrap();
        let mirrors = vec!["https://ghfast.top/".to_string(), "".to_string()];
        let list = candidates(pkg, &mirrors);
        assert_eq!(list.len(), 2, "一个源 + 一个镜像 + 一个直连 = 2 条: {:?}", list);
        assert_eq!(list[0].0, format!("https://ghfast.top/{}", pkg.sources[0].url));
        assert_eq!(list[1].0, pkg.sources[0].url, "最后必须回落直连");
    }

    /// 非 GitHub 的源不该被套镜像（套了必 404）
    #[test]
    fn non_github_source_is_not_mirrored() {
        let pkg = PACKAGES.iter().find(|p| p.id == "mkvtoolnix").unwrap();
        let list = candidates(pkg, &["https://ghfast.top/".to_string()]);
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].0, pkg.sources[0].url);
    }

    /// 真实的下载 + 解压 + 识别，一条龙跑通。
    ///
    /// 默认忽略（要联网、会拉 1MB+），需要时手动跑：
    ///   cargo test -- --ignored --nocapture
    ///
    /// 选 FLAC 是因为它只有 1.3MB，验证成本最低，但走的是和 FFmpeg
    /// 完全相同的代码路径（ureq 流式下载 → zip 解压 → 按文件名识别）。
    #[test]
    #[ignore = "需要网络"]
    fn real_download_and_extract_flac() {
        let pkg = PACKAGES.iter().find(|p| p.id == "flac").unwrap();
        let url = pkg.sources[0].url;
        eprintln!("直连下载 {url}");

        let tmp = std::env::temp_dir().join("lanzhu_fetch_test");
        let _ = std::fs::remove_dir_all(&tmp);
        std::fs::create_dir_all(&tmp).unwrap();
        let zip_path = tmp.join("flac.zip");

        let resp = ureq::get(url)
            .timeout(Duration::from_secs(180))
            .call()
            .expect("下载请求失败");
        let mut reader = resp.into_reader();
        let mut f = std::fs::File::create(&zip_path).unwrap();
        let n = std::io::copy(&mut reader, &mut f).unwrap();
        drop(f);
        eprintln!("下载完成 {} 字节", n);
        assert!(n > 300_000, "下载到的文件太小，可能是个错误页: {n}");

        let dest = tmp.join("out");
        extract(&zip_path, &dest, false).expect("解压失败");

        let mut budget = 200_000;
        assert!(
            contains_file(&dest, "flac.exe", 0, &mut budget),
            "解压后没找到 flac.exe"
        );
        eprintln!("解压并识别 flac.exe 成功 ✓");

        let _ = std::fs::remove_dir_all(&tmp);
    }
}
