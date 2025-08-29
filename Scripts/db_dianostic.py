"""
Database Connection Diagnostics Script for Horus Bridge Server
Run this script to diagnose PostgreSQL connection issues
"""

import socket
import psycopg2
import sys
import traceback
from datetime import datetime

def test_network_connectivity(host, port):
    """Test basic network connectivity to the database server"""
    print(f"\n1. Testing network connectivity to {host}:{port}")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        result = sock.connect_ex((host, int(port)))
        sock.close()
        
        if result == 0:
            print("   ✓ Network connectivity: SUCCESS")
            return True
        else:
            print("   ✗ Network connectivity: FAILED")
            print(f"     Error code: {result}")
            return False
    except Exception as e:
        print(f"   ✗ Network connectivity test failed: {e}")
        return False

def test_postgresql_service(host, port):
    """Test if PostgreSQL service is responding"""
    print(f"\n2. Testing PostgreSQL service response on {host}:{port}")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((host, int(port)))
        
        # Send a basic PostgreSQL startup message to test if it's really PostgreSQL
        startup_msg = b'\x00\x00\x00\x08\x04\xd2\x16\x2f'
        sock.send(startup_msg)
        
        # Try to receive response
        response = sock.recv(1024)
        sock.close()
        
        if response:
            print("   ✓ PostgreSQL service: RESPONDING")
            return True
        else:
            print("   ✗ PostgreSQL service: NOT RESPONDING")
            return False
            
    except Exception as e:
        print(f"   ✗ PostgreSQL service test failed: {e}")
        return False

def test_database_connection_methods(host, port, database, user, password):
    """Test different database connection methods"""
    print(f"\n3. Testing database authentication for user '{user}' on database '{database}'")
    
    connection_methods = [
        {
            "name": "Standard Connection String",
            "conn_str": f"host={host} port={port} dbname={database} user={user} password={password}"
        },
        {
            "name": "Quoted Connection String", 
            "conn_str": f"host='{host}' port='{port}' dbname='{database}' user='{user}' password='{password}'"
        },
        {
            "name": "URI Format",
            "conn_str": f"postgresql://{user}:{password}@{host}:{port}/{database}"
        }
    ]
    
    for i, method in enumerate(connection_methods):
        print(f"\n   Method {i+1}: {method['name']}")
        try:
            # Test connection with short timeout
            conn = psycopg2.connect(method['conn_str'], connect_timeout=10)
            
            # Test basic query
            cursor = conn.cursor()
            cursor.execute("SELECT version()")
            version = cursor.fetchone()[0]
            cursor.close()
            conn.close()
            
            print(f"   ✓ SUCCESS - Connected to: {version[:60]}...")
            return method['conn_str'], True
            
        except psycopg2.OperationalError as op_ex:
            print(f"   ✗ FAILED - Operational Error: {op_ex}")
            
        except psycopg2.Error as pg_ex:
            print(f"   ✗ FAILED - PostgreSQL Error: {pg_ex}")
            
        except Exception as ex:
            print(f"   ✗ FAILED - General Error: {ex}")
    
    return None, False

def test_database_permissions(conn_str):
    """Test database permissions"""
    print(f"\n4. Testing database permissions")
    try:
        conn = psycopg2.connect(conn_str, connect_timeout=10)
        cursor = conn.cursor()
        
        # Test basic read permissions
        print("   Testing SELECT permissions...")
        cursor.execute("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' LIMIT 5")
        tables = cursor.fetchall()
        print(f"   ✓ Can read {len(tables)} tables")
        
        # Test specific Horus tables
        print("   Testing Horus tables access...")
        horus_tables = ['recordings', 'frames']
        available_tables = []
        
        for table in horus_tables:
            try:
                cursor.execute(f"SELECT COUNT(*) FROM {table}")
                count = cursor.fetchone()[0]
                available_tables.append(table)
                print(f"   ✓ Table '{table}': {count} rows")
            except psycopg2.Error as e:
                print(f"   ✗ Table '{table}': Not accessible - {e}")
        
        cursor.close()
        conn.close()
        
        print(f"   Available Horus tables: {available_tables}")
        return len(available_tables) > 0
        
    except Exception as e:
        print(f"   ✗ Permission test failed: {e}")
        return False

def check_environment():
    """Check Python environment and dependencies"""
    print("5. Checking Python environment")
    
    # Check Python version
    print(f"   Python version: {sys.version}")
    
    # Check psycopg2
    try:
        import psycopg2
        print(f"   ✓ psycopg2 version: {psycopg2.__version__}")
    except ImportError:
        print("   ✗ psycopg2 not available")
        return False
    
    # Check if we're in ArcGIS Pro environment
    try:
        import arcpy
        print("   ✓ Running in ArcGIS Pro Python environment")
    except ImportError:
        print("   ⚠ Not running in ArcGIS Pro environment (may be OK)")
    
    return True

def generate_troubleshooting_report(host, port, database, user, successful_conn_str=None):
    """Generate a troubleshooting report"""
    print("\n" + "="*60)
    print("TROUBLESHOOTING REPORT")
    print("="*60)
    
    if successful_conn_str:
        print(f"✓ GOOD NEWS: Database connection successful!")
        print(f"  Working connection string: {successful_conn_str.replace(user, 'USER').replace('password=', 'password=***')}")
        print("\n  SOLUTION: Update your Python bridge server to use this connection method.")
        
    else:
        print("✗ PROBLEM: Could not establish database connection")
        print("\nPossible causes and solutions:")
        
        print("\n1. NETWORK ISSUES:")
        print("   - Check if database server is accessible from your network")
        print(f"   - Verify firewall allows connections to port {port}")
        print("   - Test: ping {host}")
        print(f"   - Test: telnet {host} {port}")
        
        print("\n2. POSTGRESQL CONFIGURATION:")
        print("   - Check postgresql.conf: listen_addresses setting")
        print("   - Check pg_hba.conf: client authentication settings")
        print("   - Ensure PostgreSQL service is running")
        
        print("\n3. CREDENTIALS:")
        print("   - Verify username and password are correct")
        print("   - Check if user has sufficient database privileges")
        print("   - Test connection from command line: psql -h {host} -p {port} -U {user} -d {database}")
        
        print("\n4. CONNECTION SETTINGS:")
        print("   - Try increasing connection timeout")
        print("   - Check if SSL is required (add sslmode=require)")
        print("   - Verify database name spelling")

def main():
    """Main diagnostic routine"""
    print("PostgreSQL Connection Diagnostics for Horus Bridge Server")
    print(f"Timestamp: {datetime.now().isoformat()}")
    print("="*60)
    
    # Configuration from your logs
    host = "10.0.10.100"
    port = "5432" 
    database = "HorusWebMoviePlayer"
    user = "pocmsro"
    password = input("Enter database password: ").strip()
    
    if not password:
        print("Error: Password is required")
        return
    
    print(f"\nTesting connection to:")
    print(f"  Host: {host}")
    print(f"  Port: {port}")
    print(f"  Database: {database}")
    print(f"  User: {user}")
    
    # Run diagnostic steps
    network_ok = test_network_connectivity(host, port)
    
    if network_ok:
        postgresql_ok = test_postgresql_service(host, port)
        
        if postgresql_ok:
            successful_conn_str, auth_ok = test_database_connection_methods(host, port, database, user, password)
            
            if auth_ok:
                permissions_ok = test_database_permissions(successful_conn_str)
                
                if permissions_ok:
                    print("\n✓ ALL TESTS PASSED - Database should work with your bridge server!")
                else:
                    print("\n⚠ Connection works but permissions may be limited")
            
        else:
            print("\n✗ PostgreSQL service not responding - check server configuration")
    else:
        print("\n✗ Network connectivity failed - check network and firewall settings")
    
    # Check environment
    env_ok = check_environment()
    
    # Generate report
    successful_conn_str = None  # You'd get this from test_database_connection_methods if successful
    generate_troubleshooting_report(host, port, database, user, successful_conn_str)

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nDiagnostics interrupted by user")
    except Exception as e:
        print(f"\nDiagnostic script failed: {e}")
        print(f"Traceback: {traceback.format_exc()}")