#!/bin/bash

echo "🚀 Testing BurbujaEngine Priority System"
echo "========================================"

BASE_URL="http://localhost:5220"

echo ""
echo "1. Testing basic engine status..."
curl -s "$BASE_URL/engine/status" | jq -r '.state' && echo "✅ Engine is running"

echo ""
echo "2. Testing priority system info..."
curl -s "$BASE_URL/engine/priorities" | jq '.priority_system_info.version' && echo "✅ Priority system accessible"

echo ""
echo "3. Testing engine health..."
curl -s "$BASE_URL/engine/health" | jq -r '.status' && echo "✅ Health check working"

echo ""
echo "4. Running stress test..."
echo "This may take a few minutes..."
curl -X POST -s "$BASE_URL/engine/stress-test" | jq '.success, .summary' && echo "✅ Stress test completed"

echo ""
echo "🎉 All tests completed!"
