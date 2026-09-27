"""End-to-end API checks against a fresh, isolated SQL Server LocalDB database.

Run from the repository root: python tests/api_smoke.py --dotnet PATH_TO_DOTNET
The test database name is generated per run and intentionally kept for inspection.
"""

import argparse
import http.cookiejar
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid


ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "U1.Business" / "U1.Business.csproj"


class Client:
    def __init__(self, base):
        self.base = base
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())
        )
        self.csrf = None

    def request(self, method, path, body=None, expected=200, include_csrf=True):
        headers = {}
        data = None
        if method not in ("GET", "HEAD"):
            if include_csrf:
                if self.csrf is None:
                    self.csrf = self.request("GET", "/api/csrf")["token"]
                headers["X-CSRF-TOKEN"] = self.csrf
            headers["Content-Type"] = "application/json"
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(
            self.base + path, data=data, headers=headers, method=method
        )
        try:
            response = self.opener.open(request, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            status = response.status
        assert status == expected, (method, path, status, expected, raw.decode())
        if status == 200 and path in ("/api/auth/login", "/api/auth/register", "/api/auth/logout"):
            self.csrf = None
        return normalize(json.loads(raw)) if raw else None


def normalize(value):
    if isinstance(value, list):
        return [normalize(item) for item in value]
    if isinstance(value, dict):
        return {key[:1].lower() + key[1:]: normalize(item)
                for key, item in value.items()}
    return value


def check(name, action):
    action()
    print("PASS", name, flush=True)


def run(base, database):
    guest = Client(base)
    check("separate database guard", lambda: assert_equal(
        guest.request("GET", "/api/test-environment")["database"], database
    ))
    check("anonymous catalog denied", lambda: guest.request(
        "GET", "/api/products", expected=401
    ))

    admin = Client(base)
    admin.request("POST", "/api/auth/login", {
        "email": "admin@u1.local", "password": "U1Admin!2026"
    })
    check("admin grid is database configured", lambda: assert_true(
        len(admin.request("GET", "/api/admin/grid")) >= 7
    ))
    code = "QA-" + uuid.uuid4().hex[:10].upper()
    product = {
        "code": code, "name": "Test ürünü", "description": "Aranabilir açıklama",
        "brand": "QA", "manufacturerCode": "QA-MAKER", "specialCode1": "QA-SPECIAL",
        "specialCode2": "", "imageUrl": "/images/product.svg", "stock": 5,
        "criticalStock": 2, "price": 100.00, "categoryId": 1,
    }
    check("invalid precision rejected", lambda: admin.request(
        "POST", "/api/admin/products", {**product, "price": 100.123}, expected=400
    ))
    check("negative stock rejected", lambda: admin.request(
        "POST", "/api/admin/products", {**product, "stock": -1}, expected=400
    ))
    product_id = admin.request("POST", "/api/admin/products", product)["id"]
    check("search includes special codes", lambda: assert_true(
        any(p["id"] == product_id for p in admin.request(
            "GET", "/api/products?q=QA-SPECIAL")["items"])
    ))

    buyer = Client(base)
    registration = {
        "firstName": "Ayşe", "lastName": "Test", "email": "qa-" + uuid.uuid4().hex + "@example.test",
        "phone": "0532 123 45 67", "company": "QA", "password": "QaPassword!2026",
    }
    check("invalid phone rejected", lambda: buyer.request(
        "POST", "/api/auth/register", {**registration, "phone": "----------"}, expected=400
    ))
    buyer.request("POST", "/api/auth/register", registration)
    check("CSRF token required", lambda: buyer.request(
        "POST", "/api/cart", {"productId": product_id, "quantity": 1},
        expected=400, include_csrf=False
    ))
    check("dealer cannot manage products", lambda: buyer.request(
        "POST", "/api/admin/products", product, expected=403
    ))
    check("null cart request is client error", lambda: buyer.request(
        "POST", "/api/cart", None, expected=400
    ))
    check("missing product rejected", lambda: buyer.request(
        "POST", "/api/cart", {"productId": 2147483647, "quantity": 1}, expected=404
    ))
    buyer.request("POST", "/api/cart", {"productId": product_id, "quantity": 2})
    cart = buyer.request("GET", "/api/cart")
    check("cart totals", lambda: assert_equal((cart["count"], cart["total"]), (2, 200)))
    initial = admin.request("GET", f"/api/products/{product_id}")
    admin.request("PUT", f"/api/admin/products/{product_id}", {
        **product, "stock": 1, "version": initial["rowVersion"]
    })
    insufficient = {
        "requestId": str(uuid.uuid4()), "note": "stock check",
        "lines": [{"productId": product_id, "quantity": 2, "unitPrice": 100}],
    }
    check("checkout rejects stock reduced after cart add", lambda: buyer.request(
        "POST", "/api/orders", insufficient, expected=409
    ))
    check("failed checkout preserves cart", lambda: assert_equal(
        buyer.request("GET", "/api/cart")["count"], 2
    ))
    check("stale product version rejected", lambda: admin.request(
        "PUT", f"/api/admin/products/{product_id}",
        {**product, "stock": 5, "version": initial["rowVersion"]}, expected=409
    ))
    current = admin.request("GET", f"/api/products/{product_id}")
    admin.request("PUT", f"/api/admin/products/{product_id}", {
        **product, "stock": 5, "version": current["rowVersion"]
    })
    request_id = str(uuid.uuid4())
    approval = {
        "requestId": request_id, "note": "QA order",
        "lines": [{"productId": product_id, "quantity": 2, "unitPrice": 100}],
    }
    order = buyer.request("POST", "/api/orders", approval)
    check("checkout is idempotent", lambda: assert_equal(
        buyer.request("POST", "/api/orders", approval)["id"], order["id"]
    ))
    check("stock reduced and cart cleared", lambda: (
        assert_equal(buyer.request("GET", f"/api/products/{product_id}")["stock"], 3),
        assert_equal(buyer.request("GET", "/api/cart")["count"], 0),
    ))
    old = admin.request("GET", f"/api/products/{product_id}")
    product["price"] = 120
    admin.request("PUT", f"/api/admin/products/{product_id}", {**product, "stock": 3, "version": old["rowVersion"]})
    check("order keeps original price", lambda: assert_equal(
        buyer.request("GET", f"/api/orders/{order['id']}")["items"][0]["unitPrice"], 100
    ))
    admin.request("PUT", f"/api/admin/orders/{order['id']}/status", {"status": "Reddedildi"})
    check("rejection reaches buyer and returns stock", lambda: (
        assert_equal(buyer.request("GET", f"/api/orders/{order['id']}")["order"]["status"], "Reddedildi"),
        assert_equal(buyer.request("GET", f"/api/products/{product_id}")["stock"], 5),
    ))
    admin.request("PUT", f"/api/admin/orders/{order['id']}/status", {"status": "Reddedildi"})
    check("repeat rejection does not double restock", lambda: assert_equal(
        buyer.request("GET", f"/api/products/{product_id}")["stock"], 5
    ))
    other = Client(base)
    other.request("POST", "/api/auth/login", {
        "email": "bayi@u1.local", "password": "U1Bayi!2026"
    })
    check("another dealer cannot view order", lambda: other.request(
        "GET", f"/api/orders/{order['id']}", expected=404
    ))


def assert_equal(actual, expected):
    assert actual == expected, (actual, expected)


def assert_true(value):
    assert value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--port", type=int, default=5197)
    args = parser.parse_args()
    database = "U1Business_Test_" + uuid.uuid4().hex[:12]
    base = f"http://127.0.0.1:{args.port}"
    env = os.environ.copy()
    env["ConnectionStrings__SqlServer"] = (
        rf"Server=(localdb)\MSSQLLocalDB;Database={database};"
        "Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15"
    )
    env["U1_TEST_DATABASE"] = database
    dll = PROJECT.parent / "bin" / "Release" / "net10.0" / "U1.Business.dll"
    if not dll.exists():
        raise FileNotFoundError(f"Build Release first: {dll}")
    command = [args.dotnet, str(dll), "--urls", base, "--environment", "Development"]
    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=PROJECT.parent, env=env, stdout=log,
                                   stderr=subprocess.STDOUT)
        try:
            for _ in range(120):
                if process.poll() is not None:
                    raise RuntimeError(f"Server exited with code {process.returncode}")
                try:
                    urllib.request.urlopen(base + "/api/test-environment", timeout=1).close()
                    break
                except (urllib.error.URLError, TimeoutError):
                    time.sleep(0.5)
            else:
                raise TimeoutError("Server did not start within 60 seconds")
            print("Test database:", database, flush=True)
            run(base, database)
        except Exception:
            log.flush()
            log.seek(0)
            print("Server log:\n" + log.read()[-4000:], file=sys.stderr)
            raise
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("FAIL", repr(exc), file=sys.stderr)
        raise
