package launcher

import (
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"
)

func fixtureInstallation(t *testing.T) installation {
	t.Helper()
	root := t.TempDir()
	content := filepath.Join(root, "Content")
	if err := os.MkdirAll(content, 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(content, "install-manifest.json"), []byte("{}"), 0600); err != nil {
		t.Fatal(err)
	}
	executable := filepath.Join(root, "game")
	if err := os.WriteFile(executable, []byte("fixture"), 0700); err != nil {
		t.Fatal(err)
	}
	return installation{Output: root, Installation: root, Executable: executable}
}

func TestRejectEscapedExecutableAndArguments(t *testing.T) {
	valid := fixtureInstallation(t)
	if _, err := validateInstallation(valid); err != nil {
		t.Fatal(err)
	}
	outside := filepath.Join(t.TempDir(), "outside")
	if err := os.WriteFile(outside, []byte("outside"), 0700); err != nil {
		t.Fatal(err)
	}
	bad := valid
	bad.Executable = outside
	if _, err := validateInstallation(bad); err == nil {
		t.Fatal("accepted executable outside installation")
	}
	bad = valid
	bad.Arguments = []string{"-fumo-content-dir", t.TempDir()}
	if _, err := validateInstallation(bad); err == nil {
		t.Fatal("accepted arbitrary content argument")
	}
	bad = valid
	bad.Arguments = []string{"--execute", "payload"}
	if _, err := validateInstallation(bad); err == nil {
		t.Fatal("accepted arbitrary arguments")
	}
	if runtime.GOOS != "windows" {
		link := filepath.Join(valid.Installation, "escaped-link")
		if err := os.Symlink(outside, link); err != nil {
			t.Fatal(err)
		}
		bad = valid
		bad.Executable = link
		if _, err := validateInstallation(bad); err == nil {
			t.Fatal("accepted symlink escaping installation")
		}
	}
}

func TestMissingManifestAndExecutableRefuseSavedInstall(t *testing.T) {
	value := fixtureInstallation(t)
	state := t.TempDir()
	data, _ := json.Marshal(value)
	if err := os.WriteFile(filepath.Join(state, "installation.json"), data, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.Remove(filepath.Join(value.Installation, "Content", "install-manifest.json")); err != nil {
		t.Fatal(err)
	}
	app, err := New(Config{StateDirectory: state, DefaultOutput: value.Output})
	if err != nil {
		t.Fatal(err)
	}
	if app.Snapshot().CanLaunch {
		t.Fatal("saved installation trusted despite missing manifest")
	}
}

func TestImporterSuccessPersistsAndLaunchesWithContentEnvironment(t *testing.T) {
	app, config := mockApp(t, "success")
	source := t.TempDir()
	marker := filepath.Join(t.TempDir(), "launch-marker")
	t.Setenv("FUMO_LAUNCHER_MARKER", marker)
	t.Setenv("FUMO_CONTENT_PATH", "/wrong-parent-content")
	if err := app.Install(source, ""); err != nil {
		t.Fatal(err)
	}
	waitIdle(t, app)
	if snapshot := app.Snapshot(); !snapshot.CanLaunch || snapshot.Error != "" {
		t.Fatalf("not ready: %+v", snapshot)
	}
	reopened, err := New(config)
	if err != nil {
		t.Fatal(err)
	}
	if !reopened.Snapshot().CanLaunch {
		t.Fatal("did not remember installation")
	}
	if err := reopened.Launch(); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if data, err := os.ReadFile(marker); err == nil {
			if string(data) != filepath.Join(app.Snapshot().Installation, "Content") {
				t.Fatalf("bad content environment: %q", data)
			}
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatal("mock game was not launched")
}

func TestImporterFailureDoesNotPersistResult(t *testing.T) {
	for _, mode := range []string{"exit-after-result", "malformed", "duplicate-result", "no-result", "error-with-success-exit"} {
		t.Run(mode, func(t *testing.T) {
			app, _ := mockApp(t, mode)
			if err := app.Install(t.TempDir(), ""); err != nil {
				t.Fatal(err)
			}
			waitIdle(t, app)
			view := app.Snapshot()
			if view.CanLaunch || view.Error == "" {
				t.Fatalf("bad failure state: %+v", view)
			}
			if _, err := os.Stat(app.statePath()); !os.IsNotExist(err) {
				t.Fatal("failed import saved installation")
			}
		})
	}
}

func TestOverlappingSourceRejected(t *testing.T) {
	app, _ := mockApp(t, "success")
	source := t.TempDir()
	if err := app.Install(source, filepath.Join(source, "nested-output")); err == nil {
		t.Fatal("allowed output inside original")
	}
}

func TestImporterDiagnosticsStayInLocalLog(t *testing.T) {
	app, config := mockApp(t, "stderr-failure")
	if err := app.Install(t.TempDir(), ""); err != nil {
		t.Fatal(err)
	}
	waitIdle(t, app)
	view := app.Snapshot()
	if view.Error == "" || strings.Contains(view.Error, "Traceback") || strings.Contains(view.Error, "INTERNAL_RECIPE") {
		t.Fatalf("technical diagnostics leaked into the window: %q", view.Error)
	}
	log, err := os.ReadFile(filepath.Join(config.StateDirectory, "last-error.log"))
	if err != nil || !strings.Contains(string(log), "INTERNAL_RECIPE") {
		t.Fatalf("missing diagnostic log: %v", err)
	}
}

func mockApp(t *testing.T, mode string) (*App, Config) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("Unix process fixture; Windows package is cross-compiled separately")
	}
	python, err := exec.LookPath("python3")
	if err != nil {
		t.Skip("developer process fixture requires Python 3")
	}
	root := t.TempDir()
	importer := filepath.Join(root, "content-importer")
	script := `#!PYTHON
import argparse,json,os,pathlib,sys
p=argparse.ArgumentParser();p.add_argument('--source');p.add_argument('--output');p.add_argument('--recipe');p.add_argument('--json-progress',action='store_true');a=p.parse_args()
assert (pathlib.Path(a.recipe)/'recipe.json').is_file()
mode=os.environ.get('FUMO_LAUNCHER_MOCK_MODE')
if mode=='stderr-failure':
 print('Traceback: INTERNAL_RECIPE diagnostic',file=sys.stderr);sys.exit(31)
if mode=='malformed':
 print('not-json',flush=True);sys.exit(0)
print(json.dumps({'type':'progress','phase':'verifying','completed':1,'total':2,'message':'模拟安装测试'}),flush=True)
root=pathlib.Path(a.output);(root/'Content').mkdir(parents=True)
(root/'Content'/'install-manifest.json').write_text('{}')
game=root/'fake-game';game.write_text('#!/bin/sh\nprintf "%s" "$FUMO_CONTENT_PATH" > "$FUMO_LAUNCHER_MARKER"\n');game.chmod(0o700)
result={'type':'result','installation':str(root),'executable':str(game),'arguments':[]}
if mode!='no-result': print(json.dumps(result),flush=True)
if mode=='duplicate-result': print(json.dumps(result),flush=True)
if mode=='error-with-success-exit': print(json.dumps({'type':'error'}),flush=True)
if mode=='exit-after-result': sys.exit(23)
`
	script = strings.Replace(script, "PYTHON", python, 1)
	if err := os.WriteFile(importer, []byte(script), 0700); err != nil {
		t.Fatal(err)
	}
	recipe := filepath.Join(root, "recipe.json")
	if err := os.WriteFile(recipe, []byte("{}"), 0600); err != nil {
		t.Fatal(err)
	}
	t.Setenv("FUMO_LAUNCHER_MOCK_MODE", mode)
	config := Config{Importer: importer, Recipe: recipe, StateDirectory: filepath.Join(root, "state"), DefaultOutput: filepath.Join(root, "installed")}
	app, err := New(config)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(app.Shutdown)
	return app, config
}

func waitIdle(t *testing.T, app *App) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if !app.Snapshot().Busy {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("import did not finish")
}
