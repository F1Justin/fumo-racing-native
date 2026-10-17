package main

import (
	"context"
	"crypto/rand"
	"crypto/subtle"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"os/exec"
	"os/signal"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"syscall"
	"time"

	"fumoracing/launcher/internal/launcher"
	"fumoracing/launcher/ui"
)

var version = "0.1.0"

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run() error {
	flags := flag.NewFlagSet("Fumo Racing Launcher", flag.ContinueOnError)
	headless := flags.Bool("headless", false, "Print the local UI URL without opening a browser")
	bundle := flags.String("bundle-dir", "", "Developer override for the packaged importer and recipe directory")
	state := flags.String("state-dir", "", "Developer override for the launcher state directory")
	output := flags.String("output", "", "Default game installation directory")
	if err := flags.Parse(os.Args[1:]); err != nil {
		return err
	}
	dataRoot, err := userDataDirectory()
	if err != nil {
		return err
	}
	if *state == "" {
		*state = filepath.Join(dataRoot, "Fumo Racing Launcher")
	}
	if *output == "" {
		*output = filepath.Join(dataRoot, "Fumo Racing", "Installations", "current")
	}
	executable, err := os.Executable()
	if err != nil {
		return err
	}
	packageDir := filepath.Dir(executable)
	importerName := "content-importer"
	if runtime.GOOS == "windows" {
		importerName += ".exe"
	}
	importer, recipe := filepath.Join(packageDir, importerName), filepath.Join(packageDir, "recipe.json")
	if runtime.GOOS == "darwin" && filepath.Base(packageDir) == "MacOS" && filepath.Base(filepath.Dir(packageDir)) == "Contents" {
		recipe = filepath.Join(filepath.Dir(packageDir), "Resources", "recipe.json")
	}
	if *bundle != "" {
		absolute, pathErr := filepath.Abs(*bundle)
		if pathErr != nil {
			return pathErr
		}
		importer, recipe = filepath.Join(absolute, importerName), filepath.Join(absolute, "recipe.json")
	}
	app, err := launcher.New(launcher.Config{Importer: importer, Recipe: recipe, StateDirectory: *state, DefaultOutput: *output})
	if err != nil {
		return err
	}
	defer app.Shutdown()
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		return err
	}
	defer listener.Close()
	host := listener.Addr().String()
	origin := "http://" + host
	random := make([]byte, 32)
	if _, err = rand.Read(random); err != nil {
		return err
	}
	token := hex.EncodeToString(random)
	url := origin + "/?token=" + token
	quit := make(chan struct{})
	var quitOnce sync.Once
	mux := newHandler(app, host, origin, token, func() { quitOnce.Do(func() { close(quit) }) })
	server := &http.Server{Handler: mux, ReadHeaderTimeout: 10 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16384}
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancel()
	go func() {
		select {
		case <-ctx.Done():
		case <-quit:
		}
		server.Close()
	}()
	fmt.Println(url)
	if !*headless {
		go func() {
			if err := openURL(url); err != nil {
				fmt.Fprintln(os.Stderr, "无法自动打开窗口，请打开上述本地地址。")
			}
		}()
	}
	err = server.Serve(listener)
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}

func newHandler(app *launcher.App, host, origin, token string, quit func()) http.Handler {
	mux := http.NewServeMux()
	respond := func(w http.ResponseWriter, value any) {
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_ = json.NewEncoder(w).Encode(value)
	}
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/" || r.Method != http.MethodGet || r.Host != host || !constantEqual(r.URL.Query().Get("token"), token) {
			http.Error(w, "Forbidden", http.StatusForbidden)
			return
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		w.Header().Set("Referrer-Policy", "no-referrer")
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("Content-Security-Policy", "default-src 'none'; script-src 'nonce-"+token+"'; style-src 'nonce-"+token+"'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'")
		_, _ = io.WriteString(w, strings.NewReplacer("__TOKEN__", token, "__NONCE__", token, "__VERSION__", version).Replace(ui.HTML))
	})
	mux.HandleFunc("/api/", func(w http.ResponseWriter, r *http.Request) {
		if r.Host != host || !constantEqual(r.Header.Get("X-Fumo-Token"), token) || (r.Header.Get("Origin") != "" && r.Header.Get("Origin") != origin) {
			http.Error(w, "Forbidden", http.StatusForbidden)
			return
		}
		if r.Method == http.MethodGet && r.URL.Path == "/api/state" {
			respond(w, app.Snapshot())
			return
		}
		if r.Method != http.MethodPost {
			http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}
		r.Body = http.MaxBytesReader(w, r.Body, 16384)
		var err error
		switch r.URL.Path {
		case "/api/install":
			var input struct {
				SourcePath string `json:"sourcePath"`
				OutputPath string `json:"outputPath"`
			}
			if err = decode(r.Body, &input); err == nil {
				err = app.Install(input.SourcePath, input.OutputPath)
			}
		case "/api/select":
			var input struct {
				Kind string `json:"kind"`
			}
			if err = decode(r.Body, &input); err == nil {
				if app.Snapshot().Busy {
					err = errors.New("正在安装，请稍候")
				} else {
					var path string
					path, err = choosePath(r.Context(), input.Kind)
					if err == nil {
						respond(w, map[string]any{"path": path, "cancelled": path == ""})
						return
					}
				}
			}
		case "/api/launch":
			err = app.Launch()
		case "/api/quit":
			if app.Snapshot().Busy {
				err = errors.New("安装尚未完成，请等待安装结束后退出")
			} else {
				respond(w, map[string]bool{"ok": true})
				if quit != nil {
					time.AfterFunc(100*time.Millisecond, quit)
				}
				return
			}
		default:
			http.Error(w, "Not found", http.StatusNotFound)
			return
		}
		if err != nil {
			app.SetError(err)
			w.Header().Set("Content-Type", "application/json; charset=utf-8")
			w.WriteHeader(http.StatusBadRequest)
			respond(w, map[string]string{"error": err.Error()})
			return
		}
		respond(w, map[string]bool{"ok": true})
	})
	return mux
}

func decode(reader io.Reader, value any) error {
	decoder := json.NewDecoder(reader)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(value); err != nil {
		return errors.New("操作信息无效，请重新选择")
	}
	if err := decoder.Decode(new(any)); err != io.EOF {
		return errors.New("操作信息无效，请重试")
	}
	return nil
}

func constantEqual(a, b string) bool { return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1 }

func userDataDirectory() (string, error) {
	if runtime.GOOS == "windows" {
		if path := os.Getenv("LOCALAPPDATA"); path != "" {
			return path, nil
		}
		return os.UserConfigDir()
	}
	home, err := os.UserHomeDir()
	if err != nil {
		return "", err
	}
	if runtime.GOOS == "darwin" {
		return filepath.Join(home, "Library", "Application Support"), nil
	}
	if path := os.Getenv("XDG_DATA_HOME"); filepath.IsAbs(path) {
		return path, nil
	}
	return filepath.Join(home, ".local", "share"), nil
}

func openURL(url string) error {
	switch runtime.GOOS {
	case "darwin":
		return exec.Command("/usr/bin/open", url).Run()
	case "windows":
		return exec.Command("rundll32.exe", "url.dll,FileProtocolHandler", url).Run()
	default:
		return exec.Command("xdg-open", url).Run()
	}
}
