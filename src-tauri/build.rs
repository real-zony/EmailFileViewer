fn main() {
    // D11 的命令登记为应用权限（allow-<命令名>），由 capabilities 逐个授予主窗口
    let manifest = tauri_build::AppManifest::new().commands(&[
        "app_ready",
        "open_with_dialog",
        "open_nested",
        "close_document",
        "save_attachment",
        "save_all_attachments",
        "open_attachment",
    ]);
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(manifest))
        .expect("failed to run tauri-build");
}
