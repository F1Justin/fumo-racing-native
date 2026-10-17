package main

import (
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"

	"fumoracing/launcher/internal/launcher"
)

func TestLocalAPIRejectsForeignOriginsHostsAndTokens(t *testing.T) {
	root := t.TempDir()
	app, err := launcher.New(launcher.Config{StateDirectory: filepath.Join(root, "state"), DefaultOutput: filepath.Join(root, "game")})
	if err != nil {
		t.Fatal(err)
	}
	handler := newHandler(app, "127.0.0.1:1234", "http://127.0.0.1:1234", "expected-token", nil)
	for _, test := range []struct {
		name, host, token, origin string
		status                    int
	}{
		{"valid", "127.0.0.1:1234", "expected-token", "http://127.0.0.1:1234", http.StatusOK},
		{"native-no-origin", "127.0.0.1:1234", "expected-token", "", http.StatusOK},
		{"no-token", "127.0.0.1:1234", "", "", http.StatusForbidden},
		{"foreign-origin", "127.0.0.1:1234", "expected-token", "https://example.invalid", http.StatusForbidden},
		{"foreign-host", "evil.invalid", "expected-token", "", http.StatusForbidden},
	} {
		t.Run(test.name, func(t *testing.T) {
			request := httptest.NewRequest("GET", "http://127.0.0.1:1234/api/state", nil)
			request.Host = test.host
			request.Header.Set("X-Fumo-Token", test.token)
			request.Header.Set("Origin", test.origin)
			response := httptest.NewRecorder()
			handler.ServeHTTP(response, request)
			if response.Code != test.status {
				t.Fatalf("status %d, wanted %d", response.Code, test.status)
			}
		})
	}
	request := httptest.NewRequest("POST", "http://127.0.0.1:1234/api/install", strings.NewReader(`{"sourcePath":"/tmp/source","recipe":"/tmp/arbitrary"}`))
	request.Header.Set("X-Fumo-Token", "expected-token")
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != http.StatusBadRequest {
		t.Fatal("UI was allowed to override fixed recipe")
	}
}
