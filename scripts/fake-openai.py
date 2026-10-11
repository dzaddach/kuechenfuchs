#!/usr/bin/env python3
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MAX_REQUEST_BYTES = 256 * 1024


def read_exactly(stream, length):
    body = bytearray()
    while len(body) < length:
        part = stream.read(length - len(body))
        if not part:
            raise ValueError("request body ended before its declared length")
        body.extend(part)
    return bytes(body)


def read_request_body(stream, headers):
    transfer_encoding = headers.get("Transfer-Encoding", "").lower()
    if "chunked" in [value.strip() for value in transfer_encoding.split(",")]:
        body = bytearray()
        while True:
            line = stream.readline(8192)
            if not line.endswith(b"\r\n"):
                raise ValueError("invalid chunk-size line")
            try:
                chunk_size = int(line[:-2].split(b";", 1)[0], 16)
            except ValueError as exception:
                raise ValueError("invalid chunk size") from exception
            if chunk_size < 0 or len(body) + chunk_size > MAX_REQUEST_BYTES:
                raise ValueError("request body exceeds the configured limit")
            if chunk_size == 0:
                while True:
                    trailer = stream.readline(8192)
                    if trailer == b"\r\n":
                        return bytes(body)
                    if not trailer or not trailer.endswith(b"\r\n"):
                        raise ValueError("invalid chunk trailer")
            body.extend(read_exactly(stream, chunk_size))
            if read_exactly(stream, 2) != b"\r\n":
                raise ValueError("invalid chunk terminator")

    try:
        content_length = int(headers.get("Content-Length", ""))
    except ValueError as exception:
        raise ValueError("a valid Content-Length or chunked encoding is required") from exception
    if content_length < 0 or content_length > MAX_REQUEST_BYTES:
        raise ValueError("request body exceeds the configured limit")
    return read_exactly(stream, content_length)


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.end_headers()

    def do_POST(self):
        if self.path != "/v1/chat/completions":
            self.send_error(404)
            return

        try:
            request = json.loads(read_request_body(self.rfile, self.headers))
        except (ValueError, json.JSONDecodeError):
            self.send_error(400, "Invalid request body")
            return
        prompt = json.loads(request["messages"][1]["content"])["Prompt"]

        if prompt == "FAKE: timeout":
            time.sleep(10)
            self.send_json(200, self.completion())
            return
        if prompt == "FAKE: delayed success":
            time.sleep(6)
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


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
