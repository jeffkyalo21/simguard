import os
import sys
from pathlib import Path

# Make "src" importable from the ml-service root.
ML_SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(ML_SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(ML_SERVICE_ROOT))

# Tests run as Development so that apps built without a shared secret start.
# tests/test_shared_secret.py covers the other environments.
os.environ.setdefault("ML_ENVIRONMENT", "Development")
