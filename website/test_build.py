from io import BytesIO
from unittest import TestCase, main
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler

from build import verify_installer_download


class ReleaseDownloadChecks(TestCase):
    asset = {"id": 123, "size": 1024, "browser_download_url": "https://github.com/example/download/setup.exe"}

    def gateway_error(self, status):
        return HTTPError(self.asset["browser_download_url"], status, "Test HTTP error", None, None)

    def response(self, status, headers, body=b""):
        response = BytesIO(body)
        response.status = status
        response.headers = headers
        return response

    @patch("build.sleep")
    @patch("build.urlopen")
    def test_retries_transient_gateway_error(self, request, _sleep):
        request.side_effect = [self.gateway_error(504), self.response(200, {"Content-Length": "1024"})]
        verify_installer_download(self.asset)
        self.assertEqual([call.args[0].method for call in request.call_args_list], ["HEAD", "HEAD"])

    @patch("build.sleep")
    @patch("build.urlopen")
    def test_checks_binary_through_asset_api_after_retries(self, request, _sleep):
        request.side_effect = [self.gateway_error(504)] * 3 + [
            self.response(206, {"Content-Range": "bytes 0-1/1024"}, b"MZ")]
        verify_installer_download(self.asset, "test-token")
        fallback = request.call_args.args[0]
        self.assertEqual(fallback.full_url, "https://api.github.com/repos/dwolken/inklane/releases/assets/123")
        self.assertEqual(fallback.get_header("Range"), "bytes=0-1")
        self.assertEqual(fallback.get_header("Authorization"), "Bearer test-token")
        redirect = HTTPRedirectHandler().redirect_request(fallback, None, 302, "Found", {},
                                                           "https://release-assets.githubusercontent.com/setup.exe")
        self.assertIsNone(redirect.get_header("Authorization"))

    @patch("build.sleep")
    @patch("build.urlopen")
    def test_rejects_non_executable_fallback(self, request, _sleep):
        request.side_effect = [self.gateway_error(504)] * 3 + [
            self.response(206, {"Content-Range": "bytes 0-1/1024"}, b"{}")]
        with self.assertRaises(ValueError):
            verify_installer_download(self.asset)

    @patch("build.urlopen")
    def test_missing_public_download_fails_without_retry(self, request):
        request.side_effect = self.gateway_error(404)
        with self.assertRaises(HTTPError):
            verify_installer_download(self.asset)
        request.assert_called_once()


if __name__ == "__main__":
    main()
