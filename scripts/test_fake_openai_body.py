import importlib.util
import io
import unittest
from email.message import Message
from pathlib import Path


module_path = Path(__file__).with_name("fake-openai.py")
spec = importlib.util.spec_from_file_location("fake_openai", module_path)
fake_openai = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fake_openai)


class FakeOpenAiRequestBodyTests(unittest.TestCase):
    def test_reads_content_length_body(self):
        body = b'{"message":"request"}'
        headers = Message()
        headers["Content-Length"] = str(len(body))

        self.assertEqual(body, fake_openai.read_request_body(io.BytesIO(body), headers))

    def test_reads_chunked_body_with_extensions_and_trailers(self):
        body = b'{"message":"chunked request"}'
        chunks = body[:7], body[7:]
        encoded = (
            f"{len(chunks[0]):X};test=yes\r\n".encode()
            + chunks[0]
            + b"\r\n"
            + f"{len(chunks[1]):X}\r\n".encode()
            + chunks[1]
            + b"\r\n0\r\nX-Test: complete\r\n\r\n"
        )
        headers = Message()
        headers["Transfer-Encoding"] = "chunked"

        self.assertEqual(body, fake_openai.read_request_body(io.BytesIO(encoded), headers))

    def test_rejects_incomplete_body(self):
        headers = Message()
        headers["Content-Length"] = "5"

        with self.assertRaisesRegex(ValueError, "ended before"):
            fake_openai.read_request_body(io.BytesIO(b"{}"), headers)


if __name__ == "__main__":
    unittest.main()
