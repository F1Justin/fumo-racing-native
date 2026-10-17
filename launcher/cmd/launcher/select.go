package main

import (
	"context"
	"errors"
	"os/exec"
	"runtime"
	"strings"
)

// Native selection returns paths directly, so large original archives never pass
// through a browser upload or leave this machine.
func choosePath(ctx context.Context, kind string) (string, error) {
	if kind != "sourceZip" && kind != "sourceDirectory" && kind != "outputDirectory" {
		return "", errors.New("请选择原版压缩包或文件夹")
	}
	var command *exec.Cmd
	title := "选择原版游戏文件夹"
	if kind == "sourceZip" {
		title = "选择原版游戏 ZIP 压缩包"
	}
	if kind == "outputDirectory" {
		title = "选择安装位置"
	}
	switch runtime.GOOS {
	case "darwin":
		script := `POSIX path of (choose folder with prompt "` + title + `")`
		if kind == "sourceZip" {
			script = `POSIX path of (choose file with prompt "` + title + `" of type {"zip"})`
		}
		command = exec.CommandContext(ctx, "/usr/bin/osascript", "-e", script)
	case "windows":
		script := `Add-Type -AssemblyName System.Windows.Forms; `
		if kind == "sourceZip" {
			script += `$d = New-Object System.Windows.Forms.OpenFileDialog; $d.Title = '选择原版游戏 ZIP 压缩包'; $d.Filter = 'ZIP (*.zip)|*.zip'; if ($d.ShowDialog() -eq 'OK') { [Console]::OutputEncoding = [Text.UTF8Encoding]::new(); [Console]::Write($d.FileName) }`
		} else {
			script += `$d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = '` + title + `'; if ($d.ShowDialog() -eq 'OK') { [Console]::OutputEncoding = [Text.UTF8Encoding]::new(); [Console]::Write($d.SelectedPath) }`
		}
		command = exec.CommandContext(ctx, "powershell.exe", "-NoProfile", "-STA", "-NonInteractive", "-Command", script)
	default:
		if path, err := exec.LookPath("zenity"); err == nil {
			arguments := []string{"--file-selection", "--title=" + title}
			if kind == "sourceZip" {
				arguments = append(arguments, "--file-filter=ZIP | *.zip")
			} else {
				arguments = append(arguments, "--directory")
			}
			command = exec.CommandContext(ctx, path, arguments...)
		} else if path, err := exec.LookPath("kdialog"); err == nil {
			if kind == "sourceZip" {
				command = exec.CommandContext(ctx, path, "--getopenfilename", ".", "*.zip")
			} else {
				command = exec.CommandContext(ctx, path, "--getexistingdirectory", ".")
			}
		} else {
			return "", errors.New("系统没有可用的文件选择窗口。请在高级选项中填写原版游戏的完整路径。")
		}
	}
	data, err := command.Output()
	if err != nil {
		var exit *exec.ExitError
		if errors.As(err, &exit) {
			if (runtime.GOOS == "darwin" && strings.Contains(string(exit.Stderr), "-128")) || (runtime.GOOS == "linux" && exit.ExitCode() == 1) {
				return "", nil
			}
		}
		return "", errors.New("无法打开文件选择窗口，请在高级选项中填写完整路径")
	}
	return strings.TrimSpace(string(data)), nil
}
