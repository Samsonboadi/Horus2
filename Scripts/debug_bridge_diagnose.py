"""
Horus Media Server Connectivity Diagnostic Script
Run this to diagnose why image retrieval is failing with WinError 10061
"""

import socket
import urllib.request
import urllib.error
import time
import sys
from datetime import datetime

def test_basic_connectivity(host, port, timeout=10):
    """Test basic TCP connectivity"""
    print(f"\n1. Testing TCP connectivity to {host}:{port}")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(timeout)
        result = sock.connect_ex((host, port))
        sock.close()
        
        if result == 0:
            print(f"   ✓ TCP connection: SUCCESS")
            return True
        else:
            print(f"   ✗ TCP connection: FAILED (error code: {result})")
            print(f"     This means the server is not accepting connections on port {port}")
            return False
    except Exception as e:
        print(f"   ✗ TCP test failed: {e}")
        return False

def test_http_service(base_url, timeout=10):
    """Test HTTP service availability"""
    print(f"\n2. Testing HTTP service at {base_url}")
    
    test_endpoints = [
        "",           # Root
        "health",     # Health check
        "web/",       # Web interface
        "api/",       # API
    ]
    
    working_endpoints = []
    
    for endpoint in test_endpoints:
        test_url = f"{base_url.rstrip('/')}/{endpoint}".rstrip('/')
        print(f"   Testing: {test_url}")
        
        try:
            req = urllib.request.Request(test_url, headers={
                'User-Agent': 'HorusConnectivityTest/1.0'
            })
            
            with urllib.request.urlopen(req, timeout=timeout) as response:
                status = response.status
                if status in [200, 301, 302]:
                    print(f"     ✓ Status {status}: Available")
                    working_endpoints.append(endpoint)
                else:
                    print(f"     ⚠ Status {status}: Responded but with error")
                    
        except urllib.error.HTTPError as he:
            if he.code in [401, 403, 404]:
                print(f"     ⚠ Status {he.code}: Server responding (auth/not found)")
                working_endpoints.append(endpoint)
            else:
                print(f"     ✗ HTTP Error {he.code}: {he.reason}")
                
        except urllib.error.URLError as ue:
            print(f"     ✗ URL Error: {ue.reason}")
            
        except Exception as e:
            print(f"     ✗ Request failed: {e}")
    
    return working_endpoints

def test_horus_image_endpoint(base_url, timeout=15):
    """Test the specific endpoint that's failing in your logs"""
    print(f"\n3. Testing specific Horus image endpoint that's failing")
    
    # From your error: "/web/images/5/5503e24d-50fb-455f-beeb-1c65bf0c7374?mode=spherical&size=600x600&yaw=0&pitch=-30&hor_fov=90"
    test_path = "/web/images/5/5503e24d-50fb-455f-beeb-1c65bf0c7374"
    test_params = "mode=spherical&size=600x600&yaw=0&pitch=-30&hor_fov=90"
    test_url = f"{base_url.rstrip('/')}{test_path}?{test_params}"
    
    print(f"   Testing exact failing URL: {test_url}")
    
    try:
        req = urllib.request.Request(test_url, headers={
            'User-Agent': 'HorusConnectivityTest/1.0'
        })
        
        with urllib.request.urlopen(req, timeout=timeout) as response:
            print(f"   ✓ SUCCESS: Status {response.status}")
            print(f"   Content-Type: {response.headers.get('Content-Type', 'unknown')}")
            print(f"   Content-Length: {response.headers.get('Content-Length', 'unknown')}")
            return True
            
    except urllib.error.HTTPError as he:
        print(f"   ✗ HTTP Error {he.code}: {he.reason}")
        if he.code == 404:
            print("     This specific image/frame might not exist")
        elif he.code in [401, 403]:
            print("     Authentication required or forbidden")
        return False
        
    except urllib.error.URLError as ue:
        print(f"   ✗ URL Error: {ue.reason}")
        if "10061" in str(ue.reason):
            print("     🚨 CONNECTION REFUSED - Server is not accepting connections!")
        return False
        
    except Exception as e:
        print(f"   ✗ Request failed: {e}")
        return False

def test_alternative_urls():
    """Test alternative URL configurations"""
    print(f"\n4. Testing alternative URL configurations")
    
    base_configs = [
        "http://10.0.10.100:5050",
        "http://10.0.10.100:5050/",
        "http://10.0.10.100:5050/web",
        "http://10.0.10.100:5050/web/",
        "https://10.0.10.100:5050/web/",  # Try HTTPS
        "http://localhost:5050/web/",      # Try localhost
        "http://127.0.0.1:5050/web/",     # Try loopback
    ]
    
    working_configs = []
    
    for config in base_configs:
        print(f"   Testing: {config}")
        try:
            req = urllib.request.Request(config, headers={'User-Agent': 'HorusTest/1.0'})
            with urllib.request.urlopen(req, timeout=5) as response:
                if response.status in [200, 301, 302]:
                    print(f"     ✓ Working: Status {response.status}")
                    working_configs.append(config)
                else:
                    print(f"     ⚠ Status {response.status}")
        except Exception as e:
            print(f"     ✗ Failed: {str(e)[:50]}...")
    
    return working_configs

def check_local_services():
    """Check what services are running locally"""
    print(f"\n5. Checking local services")
    
    # Common ports to check
    test_ports = [5050, 8080, 80, 443, 5000, 8000, 3000]
    
    print("   Scanning common ports on 10.0.10.100:")
    open_ports = []
    
    for port in test_ports:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(2)
            result = sock.connect_ex(("10.0.10.100", port))
            sock.close()
            
            if result == 0:
                print(f"     ✓ Port {port}: OPEN")
                open_ports.append(port)
            else:
                print(f"     ✗ Port {port}: CLOSED")
        except:
            print(f"     ✗ Port {port}: ERROR")
    
    return open_ports

def compare_with_working_script():
    """Compare environment with your working standalone script"""
    print(f"\n6. Environment comparison with working script")
    
    print(f"   Python version: {sys.version}")
    print(f"   Python executable: {sys.executable}")
    
    # Check if we're in the same environment as the working script
    is_arcgis_python = "arcgispro-py3" in sys.executable.lower()
    print(f"   ArcGIS Pro Python: {'✓ YES' if is_arcgis_python else '✗ NO'}")
    
    # Check for required modules
    required_modules = ['horus_media', 'horus_db', 'horus_camera', 'psycopg2']
    
    for module in required_modules:
        try:
            __import__(module)
            print(f"   ✓ {module}: Available")
        except ImportError:
            print(f"   ✗ {module}: Missing")

def generate_troubleshooting_report(tcp_ok, http_endpoints, image_endpoint_ok, working_configs, open_ports):
    """Generate comprehensive troubleshooting report"""
    print("\n" + "=" * 80)
    print("HORUS CONNECTIVITY TROUBLESHOOTING REPORT")
    print("=" * 80)
    
    if not tcp_ok:
        print("🚨 CRITICAL ISSUE: Cannot establish TCP connection to Horus server")
        print("\nPOSSIBLE CAUSES:")
        print("1. Horus media server is not running on 10.0.10.100")
        print("2. Firewall is blocking port 5050")
        print("3. Network routing issues")
        print("4. Server has moved to different IP/port")
        
        print("\nIMMEDIATE ACTIONS:")
        print("1. Check if Horus media server is running:")
        print("   - Log into 10.0.10.100")
        print("   - Check if the Horus service/process is running")
        print("   - Check service logs for startup errors")
        
        print("2. Test network connectivity:")
        print("   - From command line: telnet 10.0.10.100 5050")
        print("   - From browser: http://10.0.10.100:5050")
        
        if open_ports:
            print(f"3. Alternative ports found open: {open_ports}")
            print("   - Check if Horus moved to one of these ports")
        
    elif not http_endpoints:
        print("🚨 PARTIAL ISSUE: TCP works but HTTP service not responding")
        print("\nPOSSIBLE CAUSES:")
        print("1. Horus server started but HTTP service crashed")
        print("2. Server is binding to localhost only (not 0.0.0.0)")
        print("3. HTTP service on different port")
        
        print("\nIMMEDIATE ACTIONS:")
        print("1. Check Horus server configuration for bind address")
        print("2. Check server logs for HTTP service errors")
        print("3. Try accessing from the server itself: curl localhost:5050")
        
    elif not image_endpoint_ok:
        print("🚨 SPECIFIC ISSUE: HTTP works but image endpoint fails")
        print("\nPOSSIBLE CAUSES:")
        print("1. Image service component not running")
        print("2. Authentication required for image endpoints")
        print("3. Specific frame/image doesn't exist")
        
        print("\nIMMEDIATE ACTIONS:")
        print("1. Check if authentication is required")
        print("2. Try a different frame ID")
        print("3. Check Horus image service logs")
        
    else:
        print("✅ CONNECTIVITY LOOKS GOOD")
        print("\nHorus server appears to be accessible and responding.")
        print("The WinError 10061 might be intermittent or timing-related.")
        
        print("\nRECOMMENDED ACTIONS:")
        print("1. Increase timeout values in the bridge server")
        print("2. Add retry logic for image requests")
        print("3. Check for network stability issues")
    
    if working_configs:
        print(f"\n✅ WORKING URL CONFIGURATIONS FOUND:")
        for config in working_configs:
            print(f"   {config}")
        print("\nTRY: Update your bridge server to use one of these URLs")
    
    print("\nNEXT STEPS:")
    print("1. Fix the connectivity issue identified above")
    print("2. Test again using: python -m horus_connectivity_diagnostic")
    print("3. If connectivity is fixed, restart your bridge server")
    print("4. Test image retrieval from your ArcGIS add-in")

def main():
    """Main diagnostic routine"""
    print("Horus Media Server Connectivity Diagnostics")
    print(f"Timestamp: {datetime.now().isoformat()}")
    print("=" * 80)
    
    # Configuration from your logs
    horus_host = "10.0.10.100" 
    horus_port = 5050
    horus_base_url = f"http://{horus_host}:{horus_port}"
    horus_full_url = f"{horus_base_url}/web/"
    
    print(f"Testing Horus media server at: {horus_full_url}")
    print(f"This is the same server your bridge is trying to reach")
    
    # Run all diagnostic tests
    tcp_ok = test_basic_connectivity(horus_host, horus_port)
    
    http_endpoints = []
    if tcp_ok:
        http_endpoints = test_http_service(horus_base_url)
    
    image_endpoint_ok = False
    if http_endpoints:
        image_endpoint_ok = test_horus_image_endpoint(horus_base_url)
    
    working_configs = test_alternative_urls()
    open_ports = check_local_services()
    
    compare_with_working_script()
    
    # Generate comprehensive report
    generate_troubleshooting_report(tcp_ok, http_endpoints, image_endpoint_ok, working_configs, open_ports)
    
    print(f"\n" + "=" * 80)
    print("DIAGNOSTIC SUMMARY")
    print("=" * 80)
    print(f"TCP Connectivity: {'✓ OK' if tcp_ok else '✗ FAILED'}")
    print(f"HTTP Service: {'✓ OK' if http_endpoints else '✗ FAILED'}")
    print(f"Image Endpoint: {'✓ OK' if image_endpoint_ok else '✗ FAILED'}")
    print(f"Working Configs: {len(working_configs)} found")
    print(f"Open Ports: {open_ports}")
    
    if tcp_ok and http_endpoints and image_endpoint_ok:
        print("\n🎉 RESULT: Horus server appears to be working correctly!")
        print("   The WinError 10061 in your bridge might be intermittent.")
        print("   Consider adding retry logic or increasing timeouts.")
    elif tcp_ok and http_endpoints:
        print("\n⚠️  RESULT: Server accessible but image endpoint has issues")
        print("   Check authentication or specific frame availability.")
    elif tcp_ok:
        print("\n⚠️  RESULT: Network OK but HTTP service not responding")
        print("   Check if Horus HTTP service is running properly.")
    else:
        print("\n🚨 RESULT: Server is not accessible at all")
        print("   This explains the WinError 10061 in your bridge server.")
        print("   Fix server accessibility before testing bridge again.")

if __name__ == "__main__":
    try:
        main()
        print("\n" + "=" * 80)
        print("Diagnostic completed. Press Enter to exit...")
        input()
    except KeyboardInterrupt:
        print("\nDiagnostic interrupted by user")
    except Exception as e:
        print(f"\nDiagnostic script failed: {e}")
        input("Press Enter to exit...")