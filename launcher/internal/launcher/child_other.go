//go:build !windows

package launcher

import "os/exec"

func configureChild(command *exec.Cmd) {}
