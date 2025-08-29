"""
Debug script to identify why the bridge server is crashing on startup
Save this as 'debug_bridge_startup.py' and run it
"""

import sys
import traceback
import os
from datetime import datetime

def test_imports():
    """Test all required imports"""
    print("Testing Python imports...")
    
    required_imports = [
        ("flask", "Flask"),
        ("json", "Built-in json"),
        ("logging", "Built-in logging"), 
        ("traceback", "Built-in traceback"),
        ("datetime", "Built-in datetime"),
        ("base64", "Built-in base64"),
        ("io", "Built-in io"),
        ("os", "Built-in os"),
        ("sys", "Built-in sys"),
        ("argparse", "Built-in argparse"),
        ("typing", "Built-in typing"),
        ("itertools", "Built-in itertools"),
        ("psycopg2", "PostgreSQL adapter"),
        ("socket", "Built-in socket"),
        ("time", "Built-in time")
    ]
    
    failed_imports = []
    
    for module, description in required_imports:
        try:
            __import__(module)
            print(f"   ✓ {module}: OK")
        except ImportError as e:
            print(f"   ✗ {module}: FAILED - {e}")
            failed_imports.append((module, description, str(e)))
    
    return failed_imports

def test_pil_import():
    """Test PIL/Pillow import specifically"""
    print("\nTesting PIL/Pillow...")
    try:
        from PIL import Image
        print("   ✓ PIL.Image: OK")
        return True
    except ImportError as e:
        print(f"   ✗ PIL.Image: FAILED - {e}")
        print("   Solution: conda install pillow")
        return False

def test_horus_imports():
    """Test Horus module imports"""
    print("\nTesting Horus modules...")
    
    horus_modules = [
        ("horus_media", "from horus_media import Client, Size"),
        ("horus_db", "from horus_db import Frames, Recordings, Frame, Recording"),
        ("horus_camera", "from horus_camera import SphericalCamera")
    ]
    
    available_modules = []
    for module_name, import_statement in horus_modules:
        try:
            exec(import_statement)
            print(f"   ✓ {module_name}: OK")
            available_modules.append(module_name)
        except ImportError as e:
            print(f"   ✗ {module_name}: FAILED - {e}")
    
    return len(available_modules) == len(horus_modules), available_modules

def test_connection_settings():
    """Test Connection_settings import"""
    print("\nTesting Connection_settings module...")
    try:
        from Connection_settings import connection_settings, external_data
        print("   ✓ Connection_settings: OK")
        return True
    except ImportError as e:
        print(f"   ✗ Connection_settings: FAILED - {e}")
        print("   This is OK - bridge will work without it")
        return False

def test_flask_app_creation():
    """Test Flask app creation"""
    print("\nTesting Flask app creation...")
    try:
        from flask import Flask, request, jsonify
        app = Flask(__name__)
        print("   ✓ Flask app creation: OK")
        return True
    except Exception as e:
        print(f"   ✗ Flask app creation: FAILED - {e}")
        return False

def check_python_environment():
    """Check Python environment details"""
    print(f"\nPython Environment Check:")
    print(f"   Python version: {sys.version}")
    print(f"   Python executable: {sys.executable}")
    print(f"   Current working directory: {os.getcwd()}")
    print(f"   Python path: {sys.path[:3]}...")  # Show first 3 paths

def test_bridge_server_syntax():
    """Test if the bridge server script has syntax errors"""
    print("\nTesting bridge server script syntax...")
    
    # Try to find the bridge server script
    possible_paths = [
        "horus_bridge_server.py",
        "Scripts/horus_bridge_server.py",
        "C:/Users/samso/Source/Repos/Test/Scripts/horus_bridge_server.py"
    ]
    
    bridge_script_path = None
    for path in possible_paths:
        if os.path.exists(path):
            bridge_script_path = path
            break
    
    if not bridge_script_path:
        print("   ✗ Bridge server script not found")
        print(f"   Looked in: {possible_paths}")
        return False
    
    print(f"   Found bridge script at: {bridge_script_path}")
    
    try:
        # Try to compile the script
        with open(bridge_script_path, 'r') as f:
            script_content = f.read()
        
        compile(script_content, bridge_script_path, 'exec')
        print("   ✓ Bridge script syntax: OK")
        return True
        
    except SyntaxError as se:
        print(f"   ✗ Bridge script syntax error: {se}")
        print(f"   Line {se.lineno}: {se.text}")
        return False
    except Exception as e:
        print(f"   ✗ Bridge script check failed: {e}")
        return False

def simulate_bridge_startup():
    """Simulate bridge server startup to catch errors"""
    print("\nSimulating bridge server startup...")
    
    try:
        # Test the basic structure that should run
        print("   Testing basic Flask setup...")
        
        from flask import Flask, request, jsonify
        import json
        import logging
        
        # Configure logging like the bridge does
        logging.basicConfig(
            level=logging.INFO,
            format='%(asctime)s - %(levelname)s - %(message)s'
        )
        
        print("   ✓ Basic setup: OK")
        
        # Test creating the bridge class structure
        print("   Testing bridge class creation...")
        
        class TestBridge:
            def __init__(self):
                self.client = None
                self.db_connection = None
                self.is_connected = False
                self.db_config = {
                    "host": "10.0.10.100",
                    "port": "5432",
                    "database": "HorusWebMoviePlayer",
                    "user": "pocmsro",
                    "password": "test"
                }
        
        test_bridge = TestBridge()
        print("   ✓ Bridge class creation: OK")
        
        # Test Flask app creation
        app = Flask(__name__)
        print("   ✓ Flask app creation: OK")
        
        print("   ✓ Bridge startup simulation: SUCCESS")
        return True
        
    except Exception as e:
        print(f"   ✗ Bridge startup simulation: FAILED - {e}")
        print(f"   Traceback: {traceback.format_exc()}")
        return False

def main():
    """Main diagnostic function"""
    print("Bridge Server Startup Diagnostic")
    print(f"Timestamp: {datetime.now().isoformat()}")
    print("=" * 60)
    
    # Check environment
    check_python_environment()
    
    # Test imports
    failed_imports = test_imports()
    
    # Test PIL separately (common issue)
    pil_ok = test_pil_import()
    
    # Test Horus modules
    horus_ok, horus_modules = test_horus_imports()
    
    # Test Connection_settings
    conn_settings_ok = test_connection_settings()
    
    # Test Flask
    flask_ok = test_flask_app_creation()
    
    # Test bridge script syntax
    syntax_ok = test_bridge_server_syntax()
    
    # Simulate startup
    startup_ok = simulate_bridge_startup()
    
    # Generate report
    print("\n" + "=" * 60)
    print("STARTUP DIAGNOSTIC REPORT")
    print("=" * 60)
    
    if failed_imports:
        print("✗ CRITICAL: Missing required imports")
        for module, desc, error in failed_imports:
            print(f"   {module}: {error}")
        print("\nSOLUTION: Install missing packages:")
        print("conda install flask psycopg2-binary pillow")
        
    elif not pil_ok:
        print("✗ CRITICAL: PIL/Pillow not available")
        print("SOLUTION: conda install pillow")
        
    elif not flask_ok:
        print("✗ CRITICAL: Flask setup failed")
        print("SOLUTION: conda install flask")
        
    elif not syntax_ok:
        print("✗ CRITICAL: Bridge script has syntax errors")
        print("SOLUTION: Check bridge script for syntax issues")
        
    elif not startup_ok:
        print("✗ CRITICAL: Bridge startup simulation failed")
        print("SOLUTION: Check the error details above")
        
    else:
        print("✓ ALL STARTUP CHECKS PASSED")
        print("\nThe bridge server should start successfully.")
        print("If it's still crashing, the issue might be:")
        print("1. Script execution permissions")
        print("2. File path issues") 
        print("3. Port conflicts")
        print("4. Antivirus interference")
        
        if not horus_ok:
            print(f"\n⚠ NOTE: Only {len(horus_modules)} Horus modules available")
            print("This is OK - bridge will start but with limited functionality")
    
    print("\nRECOMMENDED ACTIONS:")
    if failed_imports or not pil_ok or not flask_ok:
        print("1. Install missing Python packages")
        print("2. Restart ArcGIS Pro")
        print("3. Try running bridge server again")
    else:
        print("1. Try running bridge server manually:")
        print("   python horus_bridge_server.py --host localhost --port 5001")
        print("2. Check for any error messages that appear")
        print("3. Verify no other service is using port 5001")

if __name__ == "__main__":
    try:
        main()
        print("\n" + "=" * 60)
        print("Diagnostic completed. Press Enter to exit...")
        input()
    except KeyboardInterrupt:
        print("\nDiagnostic interrupted by user")
    except Exception as e:
        print(f"\nDiagnostic script failed: {e}")
        print(f"Traceback: {traceback.format_exc()}")
        input("Press Enter to exit...")