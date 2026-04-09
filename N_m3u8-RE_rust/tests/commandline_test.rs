use assert_cmd::Command;
use predicates::prelude::*;

#[test]
fn test_commandline_help() {
    let mut cmd = Command::cargo_bin("N_m3u8-RE").unwrap();
    cmd.arg("--help")
        .assert()
        .success()
        .stdout(predicate::str::contains("Cross-platform DASH/HLS/MSS downloader"));
}

#[test]
fn test_commandline_version() {
    let mut cmd = Command::cargo_bin("N_m3u8-RE").unwrap();
    cmd.arg("--version")
        .assert()
        .success();
}

#[test]
fn test_commandline_required_input() {
    let mut cmd = Command::cargo_bin("N_m3u8-RE").unwrap();
    cmd.assert()
        .failure()
        .stderr(predicate::str::contains("the following required arguments were not provided: <input>"));
}

#[test]
fn test_commandline_with_input() {
    let mut cmd = Command::cargo_bin("N_m3u8-RE").unwrap();
    cmd.arg("https://example.com/playlist.m3u8")
        .assert()
        .failure(); // 应该失败，因为这不是一个有效的播放列表
}

#[test]
fn test_commandline_with_options() {
    let mut cmd = Command::cargo_bin("N_m3u8-RE").unwrap();
    cmd.arg("https://example.com/playlist.m3u8")
        .arg("--save-name")
        .arg("test")
        .arg("--thread-count")
        .arg("4")
        .assert()
        .failure(); // 应该失败，因为这不是一个有效的播放列表
}