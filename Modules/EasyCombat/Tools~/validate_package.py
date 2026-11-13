"""Combat module integrity entry point for the unified package."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('validate_framework.py')),run_name='__main__')
