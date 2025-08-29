# horus_bridge_server.py
"""
HTTP Bridge Server for Horus Media Client integration with C# ArcGIS Pro Add-in
This server provides a REST API that your C# application can call
"""

from flask import Flask, request, jsonify, send_file
import json
import logging
import traceback
from datetime import datetime
import base64
import io
import os
from typing import List, Dict, Any

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

app = Flask(__name__)

class HorusMediaBridge:
    def __init__(self):
        self.horus_client = None
        self.db_connection = None
        self.is_connected = False
        
    def connect_horus(self, host: str, port: int, username: str = None, password: str = None) -> bool:
        """Connect to Horus media server"""
        try:
            # Import and initialize Horus client
            # Replace with actual Horus client import
            import horus_media as horus
            
            self.horus_client = horus.Client(
                host=host,
                port=port,
                username=username,
                password=password
            )
            
            self.is_connected = self.horus_client.connect()
            logger.info(f"Horus connection: {'SUCCESS' if self.is_connected else 'FAILED'}")
            
            return self.is_connected
            
        except Exception as e:
            logger.error(f"Failed to connect to Horus: {e}")
            self.is_connected = False
            return False
    
    def connect_database(self, host: str, port: str, database: str, user: str, password: str) -> bool:
        """Connect to PostgreSQL database"""
        try:
            import psycopg2
            
            self.db_connection = psycopg2.connect(
                host=host,
                port=port,
                database=database,
                user=user,
                password=password
            )
            
            # Test connection
            cursor = self.db_connection.cursor()
            cursor.execute("SELECT 1")
            cursor.close()
            
            logger.info("Database connection: SUCCESS")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to database: {e}")
            return False
    
    def get_recordings(self) -> List[Dict]:
        """Get list of available recordings"""
        try:
            if self.db_connection:
                cursor = self.db_connection.cursor()
                cursor.execute("""
                    SELECT recording_id, endpoint, name, description, created_date
                    FROM recordings 
                    WHERE active = true 
                    ORDER BY created_date DESC
                """)
                
                recordings = []
                for row in cursor.fetchall():
                    recordings.append({
                        "id": row[0],
                        "endpoint": row[1],
                        "name": row[2],
                        "description": row[3],
                        "created_date": row[4].isoformat() if row[4] else None
                    })
                
                cursor.close()
                return recordings
                
            elif self.horus_client and self.is_connected:
                # Fallback to direct Horus client
                return self.horus_client.get_recordings()
                
            else:
                return []
                
        except Exception as e:
            logger.error(f"Failed to get recordings: {e}")
            return []
    
    def get_images(self, recording_endpoint: str, count: int = 5, width: int = 600, height: int = 600) -> List[Dict]:
        """Get images from a recording"""
        try:
            if not self.horus_client or not self.is_connected:
                raise Exception("Not connected to Horus server")
            
            images = self.horus_client.get_images(
                recording=recording_endpoint,
                count=count,
                width=width,
                height=height
            )
            
            # Process images for JSON response
            processed_images = []
            for i, image_data in enumerate(images):
                # Convert image to base64 for JSON transport
                if isinstance(image_data, bytes):
                    image_b64 = base64.b64encode(image_data).decode('utf-8')
                    processed_images.append({
                        "index": i,
                        "data": image_b64,
                        "format": "image/jpeg",  # Adjust based on actual format
                        "timestamp": datetime.now().isoformat()
                    })
                elif isinstance(image_data, dict):
                    # If Horus returns structured data
                    processed_images.append(image_data)
            
            return processed_images
            
        except Exception as e:
            logger.error(f"Failed to get images: {e}")
            raise
    
    def get_image_by_timestamp(self, recording_endpoint: str, timestamp: str, width: int = 600, height: int = 600) -> Dict:
        """Get specific image by timestamp"""
        try:
            if not self.horus_client or not self.is_connected:
                raise Exception("Not connected to Horus server")
            
            image_data = self.horus_client.get_image_at_time(
                recording=recording_endpoint,
                timestamp=timestamp,
                width=width,
                height=height
            )
            
            if isinstance(image_data, bytes):
                return {
                    "data": base64.b64encode(image_data).decode('utf-8'),
                    "format": "image/jpeg",
                    "timestamp": timestamp
                }
            else:
                return image_data
                
        except Exception as e:
            logger.error(f"Failed to get image by timestamp: {e}")
            raise

# Global bridge instance
bridge = HorusMediaBridge()

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint"""
    return jsonify({
        "status": "running",
        "timestamp": datetime.now().isoformat(),
        "horus_connected": bridge.is_connected,
        "database_connected": bridge.db_connection is not None
    })

@app.route('/connect', methods=['POST'])
def connect():
    """Connect to Horus server and database"""
    try:
        data = request.json
        
        # Connect to Horus
        horus_success = False
        if 'horus' in data:
            horus_config = data['horus']
            horus_success = bridge.connect_horus(
                host=horus_config.get('host', 'localhost'),
                port=horus_config.get('port', 5050),
                username=horus_config.get('username'),
                password=horus_config.get('password')
            )
        
        # Connect to database
        db_success = False
        if 'database' in data:
            db_config = data['database']
            db_success = bridge.connect_database(
                host=db_config.get('host'),
                port=db_config.get('port', '5432'),
                database=db_config.get('database'),
                user=db_config.get('user'),
                password=db_config.get('password')
            )
        
        return jsonify({
            "success": True,
            "horus_connected": horus_success,
            "database_connected": db_success,
            "message": "Connection attempt completed"
        })
        
    except Exception as e:
        logger.error(f"Connection failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e),
            "traceback": traceback.format_exc()
        }), 500

@app.route('/recordings', methods=['GET'])
def get_recordings():
    """Get list of available recordings"""
    try:
        recordings = bridge.get_recordings()
        
        return jsonify({
            "success": True,
            "data": recordings,
            "count": len(recordings)
        })
        
    except Exception as e:
        logger.error(f"Failed to get recordings: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/images', methods=['POST'])
def get_images():
    """Get images from a recording"""
    try:
        data = request.json
        
        recording_endpoint = data.get('recording_endpoint', 'Rotterdam360\\\\Ladybug5plus')
        count = data.get('count', 5)
        width = data.get('width', 600)
        height = data.get('height', 600)
        
        images = bridge.get_images(recording_endpoint, count, width, height)
        
        return jsonify({
            "success": True,
            "data": images,
            "count": len(images)
        })
        
    except Exception as e:
        logger.error(f"Failed to get images: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/image/<path:recording_endpoint>/<timestamp>', methods=['GET'])
def get_image_by_timestamp(recording_endpoint: str, timestamp: str):
    """Get specific image by timestamp"""
    try:
        width = request.args.get('width', 600, type=int)
        height = request.args.get('height', 600, type=int)
        
        image_data = bridge.get_image_by_timestamp(recording_endpoint, timestamp, width, height)
        
        return jsonify({
            "success": True,
            "data": image_data
        })
        
    except Exception as e:
        logger.error(f"Failed to get image by timestamp: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

@app.route('/disconnect', methods=['POST'])
def disconnect():
    """Disconnect from services"""
    try:
        if bridge.horus_client:
            bridge.horus_client.disconnect()
            bridge.horus_client = None
            bridge.is_connected = False
        
        if bridge.db_connection:
            bridge.db_connection.close()
            bridge.db_connection = None
        
        return jsonify({
            "success": True,
            "message": "Disconnected successfully"
        })
        
    except Exception as e:
        logger.error(f"Disconnect failed: {e}")
        return jsonify({
            "success": False,
            "error": str(e)
        }), 500

if __name__ == '__main__':
    print("=" * 60)
    print("HORUS MEDIA BRIDGE SERVER")
    print("=" * 60)
    print("Starting HTTP bridge server on http://localhost:5001")
    print("Endpoints available:")
    print("  GET  /health                 - Health check")
    print("  POST /connect                - Connect to services")
    print("  GET  /recordings             - Get recordings list")
    print("  POST /images                 - Get images from recording")
    print("  GET  /image/<endpoint>/<time> - Get image by timestamp")
    print("  POST /disconnect             - Disconnect from services")
    print("=" * 60)
    
    # Run the Flask server
    app.run(
        host='localhost',
        port=5001,
        debug=True,
        threaded=True
    )