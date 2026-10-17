package launcher

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"time"
)

type Config struct {
	Importer       string
	Recipe         string
	StateDirectory string
	DefaultOutput  string
}

type View struct {
	Phase         string  `json:"phase"`
	Busy          bool    `json:"busy"`
	Progress      float64 `json:"progress"`
	Indeterminate bool    `json:"indeterminate"`
	Message       string  `json:"message"`
	Error         string  `json:"error"`
	SourcePath    string  `json:"sourcePath,omitempty"`
	OutputPath    string  `json:"outputPath"`
	Installation  string  `json:"installation,omitempty"`
	CanLaunch     bool    `json:"canLaunch"`
}

type installation struct {
	Output       string   `json:"output"`
	Installation string   `json:"installation"`
	Executable   string   `json:"executable"`
	Arguments    []string `json:"arguments,omitempty"`
}

// Event is the importer's line-delimited JSON protocol, never a shell command.
type Event struct {
	Type         string   `json:"type"`
	Phase        string   `json:"phase,omitempty"`
	Completed    int64    `json:"completed,omitempty"`
	Total        int64    `json:"total,omitempty"`
	Message      string   `json:"message,omitempty"`
	Installation string   `json:"installation,omitempty"`
	Executable   string   `json:"executable,omitempty"`
	Arguments    []string `json:"arguments,omitempty"`
}

type App struct {
	mu        sync.Mutex
	config    Config
	view      View
	installed installation
	cancel    context.CancelFunc
	workers   sync.WaitGroup
}

func New(config Config) (*App, error) {
	if config.StateDirectory == "" || config.DefaultOutput == "" {
		return nil, errors.New("无法确定安装位置")
	}
	if err := os.MkdirAll(config.StateDirectory, 0700); err != nil {
		return nil, err
	}
	app := &App{config: config, view: View{Phase: "welcome", Message: "请选择原版 ZIP 或已解压的游戏文件夹。", OutputPath: config.DefaultOutput}}
	data, err := os.ReadFile(app.statePath())
	if err == nil {
		var saved installation
		if json.Unmarshal(data, &saved) == nil {
			if normalized, validationError := validateInstallation(saved); validationError == nil {
				app.installed = normalized
				app.view.Installation = normalized.Installation
				app.view.OutputPath = normalized.Output
				app.view.CanLaunch, app.view.Phase, app.view.Message = true, "ready", "点击“开始游戏”即可启动。"
			} else {
				app.view.Message = "原安装位置已失效，请重新导入原版游戏。"
			}
		}
	} else if !os.IsNotExist(err) {
		return nil, fmt.Errorf("无法读取安装记录：%w", err)
	}
	return app, nil
}

func (a *App) statePath() string { return filepath.Join(a.config.StateDirectory, "installation.json") }

func (a *App) Snapshot() View { a.mu.Lock(); defer a.mu.Unlock(); return a.view }

func (a *App) Install(source, output string) error {
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.view.Busy {
		return errors.New("正在安装，请稍候")
	}
	if strings.TrimSpace(source) == "" {
		return errors.New("请先选择原版游戏压缩包或文件夹")
	}
	source, err := filepath.Abs(source)
	if err != nil {
		return err
	}
	info, err := os.Stat(source)
	if err != nil {
		return errors.New("无法读取所选原版游戏，请重新选择")
	}
	if !info.IsDir() && (!info.Mode().IsRegular() || !strings.EqualFold(filepath.Ext(source), ".zip")) {
		return errors.New("请选择 ZIP 压缩包或原版游戏文件夹")
	}
	for _, required := range []string{a.config.Importer, a.config.Recipe} {
		st, checkErr := os.Stat(required)
		if checkErr != nil || !st.Mode().IsRegular() {
			return errors.New("安装工具文件不完整，请重新下载并完整解压发布包")
		}
	}
	if output == "" {
		output = a.config.DefaultOutput
	}
	output, err = filepath.Abs(output)
	if err != nil {
		return err
	}
	if source == output || (info.IsDir() && inside(source, output)) || inside(output, source) {
		return errors.New("安装位置不能与原版文件夹重叠，请选择其他位置")
	}
	if err = os.MkdirAll(filepath.Dir(output), 0700); err != nil {
		a.saveDiagnostic("create-installation-directory", err, "")
		return errors.New("无法写入安装位置，请选择有写入权限的文件夹")
	}
	ctx, cancel := context.WithCancel(context.Background())
	a.cancel = cancel
	a.view = View{Phase: "preparing", Busy: true, Indeterminate: true, Message: "正在检查原版游戏…", SourcePath: source, OutputPath: output}
	a.workers.Add(1)
	go func() { defer a.workers.Done(); defer cancel(); a.runImport(ctx, source, output) }()
	return nil
}

func (a *App) runImport(ctx context.Context, source, output string) {
	command := exec.CommandContext(ctx, a.config.Importer, "--source", source, "--output", output, "--json-progress", "--recipe", filepath.Dir(a.config.Recipe))
	command.Dir = filepath.Dir(a.config.Importer)
	configureChild(command)
	var stderr limitedBuffer
	command.Stderr = &stderr
	stdout, err := command.StdoutPipe()
	if err != nil {
		a.saveDiagnostic("prepare-importer", err, "")
		a.fail("无法开始导入，请重新打开启动工具后重试。")
		return
	}
	if err = command.Start(); err != nil {
		a.saveDiagnostic("start-importer", err, "")
		a.fail("无法启动导入程序，请重新下载并完整解压对应系统的安装包。")
		return
	}
	scanner := bufio.NewScanner(stdout)
	scanner.Buffer(make([]byte, 4096), 64*1024)
	var result *installation
	var protocolError error
	var importerMessage string
	var importerFailed bool
	for scanner.Scan() {
		var event Event
		if json.Unmarshal(scanner.Bytes(), &event) != nil {
			protocolError = errors.New("安装工具返回了无法识别的信息")
			break
		}
		switch event.Type {
		case "progress":
			a.mu.Lock()
			a.view.Phase = event.Phase
			if event.Message != "" {
				a.view.Message = truncate(event.Message, 1000)
			}
			a.view.Indeterminate = event.Total <= 0
			if event.Total > 0 {
				a.view.Progress = max(0, min(1, float64(event.Completed)/float64(event.Total)))
			}
			a.mu.Unlock()
		case "result":
			if result != nil {
				protocolError = errors.New("安装工具重复返回安装结果")
				break
			}
			result = &installation{Output: output, Installation: event.Installation, Executable: event.Executable, Arguments: event.Arguments}
		case "error":
			importerFailed = true
			importerMessage = truncate(event.Message, 1500)
		default:
			protocolError = errors.New("安装工具返回了未知信息")
		}
		if protocolError != nil {
			break
		}
	}
	if protocolError == nil {
		protocolError = scanner.Err()
	}
	if protocolError != nil {
		_ = command.Process.Kill()
	}
	// Drain after protocol failure so the subprocess cannot block on stdout.
	_, _ = io.Copy(io.Discard, stdout)
	err = command.Wait()
	if ctx.Err() != nil {
		a.fail("安装已取消，可以重新导入。")
		return
	}
	if protocolError != nil {
		a.saveDiagnostic("read-importer-progress", protocolError, stderr.String())
		a.fail("导入遇到问题，请重新打开启动工具后重试。")
		return
	}
	if err != nil || importerFailed {
		a.saveDiagnostic("import", err, stderr.String())
		message := importerMessage
		if message == "" {
			message = "导入未完成，请重新打开启动工具后重试。"
		}
		a.fail(truncate(message, 1500))
		return
	}
	if result == nil {
		a.fail("安装工具未返回完成结果，请重新下载发布包。")
		return
	}
	verified, err := validateInstallation(*result)
	if err != nil {
		a.fail(err.Error())
		return
	}
	data, err := json.MarshalIndent(verified, "", "  ")
	if err == nil {
		err = writeAtomic(a.statePath(), append(data, '\n'))
	}
	if err != nil {
		a.saveDiagnostic("save-installation", err, "")
		a.fail("无法保存安装位置，请确认当前用户的数据文件夹可写后重试。")
		return
	}
	a.mu.Lock()
	a.installed = verified
	a.view.Busy, a.view.CanLaunch, a.view.Indeterminate = false, true, false
	a.view.Installation, a.view.Phase, a.view.Message, a.view.Progress = verified.Installation, "ready", "安装完成，可以开始游戏。", 1
	a.cancel = nil
	a.mu.Unlock()
}

// Technical output stays in a bounded local log; the window receives actions
// the player can take. Atomic replacement also avoids partial diagnostics.
func (a *App) saveDiagnostic(action string, err error, stderr string) {
	data := fmt.Sprintf("time: %s\naction: %s\nerror: %v\nstderr:\n%s\n", time.Now().UTC().Format(time.RFC3339), action, err, stderr)
	_ = writeAtomic(filepath.Join(a.config.StateDirectory, "last-error.log"), []byte(data))
}

func (a *App) fail(message string) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.view.Busy, a.view.CanLaunch, a.view.Indeterminate = false, false, false
	a.view.Phase, a.view.Error, a.view.Message = "error", message, "安装未完成"
	a.cancel = nil
}

func (a *App) SetError(err error) {
	if err == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	a.view.Error = err.Error()
}

func (a *App) Shutdown() {
	a.mu.Lock()
	if a.cancel != nil {
		a.cancel()
	}
	a.mu.Unlock()
	a.workers.Wait()
}

func (a *App) Launch() error {
	a.mu.Lock()
	if a.view.Busy {
		a.mu.Unlock()
		return errors.New("安装尚未完成")
	}
	if !a.view.CanLaunch {
		a.mu.Unlock()
		return errors.New("请先导入原版游戏")
	}
	saved := a.installed
	a.mu.Unlock()
	verified, err := validateInstallation(saved)
	if err != nil {
		return err
	}
	command := exec.Command(verified.Executable, verified.Arguments...)
	command.Dir = filepath.Dir(verified.Executable)
	for _, entry := range os.Environ() {
		if !strings.EqualFold(strings.SplitN(entry, "=", 2)[0], "FUMO_CONTENT_PATH") {
			command.Env = append(command.Env, entry)
		}
	}
	command.Env = append(command.Env, "FUMO_CONTENT_PATH="+filepath.Join(verified.Installation, "Content"))
	if err = command.Start(); err != nil {
		a.saveDiagnostic("start-game", err, "")
		return errors.New("无法启动游戏，请重新打开启动工具；仍无法启动时，请重新导入原版游戏")
	}
	go command.Wait()
	a.mu.Lock()
	a.view.Error, a.view.Message = "", "游戏正在启动…"
	a.mu.Unlock()
	return nil
}

func validateInstallation(value installation) (installation, error) {
	if value.Output == "" || value.Installation == "" || value.Executable == "" {
		return value, errors.New("安装结果不完整，请重新导入")
	}
	paths := []*string{&value.Output, &value.Installation, &value.Executable}
	for _, p := range paths {
		if !filepath.IsAbs(*p) {
			return value, errors.New("安装工具返回了无效的安装位置")
		}
		normalized, err := filepath.EvalSymlinks(*p)
		if err != nil {
			return value, errors.New("安装文件缺失，请重新导入")
		}
		*p = filepath.Clean(normalized)
	}
	if !inside(value.Output, value.Installation) || !inside(value.Installation, value.Executable) || value.Installation == value.Executable {
		return value, errors.New("安装工具返回的游戏文件超出安装位置，已停止启动")
	}
	rootInfo, err := os.Stat(value.Installation)
	if err != nil || !rootInfo.IsDir() {
		return value, errors.New("游戏安装目录无效")
	}
	info, err := os.Stat(value.Executable)
	if err != nil || !info.Mode().IsRegular() {
		return value, errors.New("游戏启动文件无效")
	}
	if runtime.GOOS != "windows" && info.Mode().Perm()&0111 == 0 {
		return value, errors.New("游戏启动文件没有运行权限")
	}
	content := filepath.Join(value.Installation, "Content")
	canonicalContent, contentErr := filepath.EvalSymlinks(content)
	if contentErr != nil || !inside(value.Installation, canonicalContent) {
		return value, errors.New("游戏内容目录缺失或位于安装位置之外")
	}
	manifestPath, manifestPathError := filepath.EvalSymlinks(filepath.Join(content, "install-manifest.json"))
	if manifestPathError != nil || !inside(canonicalContent, manifestPath) {
		return value, errors.New("游戏内容的安装记录缺失或位于内容目录之外")
	}
	manifest, manifestErr := os.Stat(manifestPath)
	if manifestErr != nil || !manifest.Mode().IsRegular() {
		return value, errors.New("游戏内容的安装记录缺失，请重新导入")
	}
	if len(value.Arguments) != 0 {
		if len(value.Arguments) != 2 || value.Arguments[0] != "-fumo-content-dir" || filepath.Clean(value.Arguments[1]) != filepath.Clean(canonicalContent) {
			return value, errors.New("安装工具返回了未允许的启动参数")
		}
		value.Arguments[1] = canonicalContent
	}
	return value, nil
}

func inside(root, path string) bool {
	relative, err := filepath.Rel(root, path)
	return err == nil && relative != ".." && !strings.HasPrefix(relative, ".."+string(filepath.Separator)) && !filepath.IsAbs(relative)
}

func writeAtomic(path string, data []byte) error {
	file, err := os.CreateTemp(filepath.Dir(path), ".installation-*.tmp")
	if err != nil {
		return err
	}
	defer os.Remove(file.Name())
	if _, err = file.Write(data); err == nil {
		err = file.Sync()
	}
	closeErr := file.Close()
	if err == nil {
		err = closeErr
	}
	if err != nil {
		return err
	}
	return os.Rename(file.Name(), path)
}

func truncate(value string, limit int) string {
	runes := []rune(value)
	if len(runes) > limit {
		return string(runes[:limit]) + "…"
	}
	return value
}

type limitedBuffer struct{ data []byte }

func (buffer *limitedBuffer) Write(data []byte) (int, error) {
	length := len(data)
	buffer.data = append(buffer.data, data...)
	if len(buffer.data) > 8192 {
		buffer.data = buffer.data[len(buffer.data)-8192:]
	}
	return length, nil
}
func (buffer *limitedBuffer) String() string { return string(buffer.data) }
