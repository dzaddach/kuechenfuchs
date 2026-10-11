#!/usr/bin/env python3
import json
import time
from http.server import BaseHTTPRequestHandler, HTTPServer


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.end_headers()

    def do_POST(self):
        if self.path != "/v1/chat/completions":
            self.send_error(404)
            return

        length = int(self.headers.get("Content-Length", "0"))
        request = json.loads(self.rfile.read(length))
        prompt = json.loads(request["messages"][1]["content"])["Prompt"]

        if prompt == "FAKE: timeout":
            time.sleep(3)
            self.send_json(200, self.completion())
            return
        if prompt == "FAKE: provider rate limit":
            self.send_json(429, {"error": {"message": "test rate limit"}})
            return
        if prompt == "FAKE: invalid json":
            self.send_json(200, self.completion("not-json"))
            return

        recipe = {
            "title": "Kartoffelpfanne",
            "description": "Ein schnelles Abendessen.",
            "servings": 3,
            "preparationMinutes": 25,
            "category": "Abendessen",
            "tags": ["schnell"],
            "ingredients": [
                {
                    "displayName": "Kartoffeln",
                    "amount": 500,
                    "unitCode": "g",
                    "foodSuggestion": "Kartoffeln",
                }
            ],
            "steps": ["Kartoffeln braten."],
            "warnings": ["Bitte Garzeit prüfen."],
        }
        if prompt == "FAKE: unknown unit":
            recipe["ingredients"][0]["unitCode"] = "cup"
        elif prompt == "FAKE: invalid amount":
            recipe["ingredients"][0]["amount"] = 0
        elif prompt == "FAKE: invalid servings":
            recipe["servings"] = 0
        else:
            recipe["servings"] = json.loads(request["messages"][1]["content"]).get("DesiredServings") or 3

        self.send_json(200, self.completion(json.dumps(recipe, ensure_ascii=False)))

    def log_message(self, _format, *_args):
        return

    def completion(self, content='{"title":"Kartoffelpfanne"}'):
        return {
            "choices": [{"message": {"content": content}}],
            "usage": {"prompt_tokens": 6500, "completion_tokens": 1000},
        }

    def send_json(self, status, value):
        body = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except BrokenPipeError:
            return


HTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
