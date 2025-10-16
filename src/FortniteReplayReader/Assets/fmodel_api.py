import subprocess
import os

FMODEL_PATH = r"C:\Users\enoch\Downloads\FModel\FModel.exe"
EXPORT_LIST = r"C:\Users\enoch\Downloads\ExportList_AllParents.txt"

with open(EXPORT_LIST, 'r') as f:
    paths = f.readlines()

for i, path in enumerate(paths):
    path = path.strip()
    print(f"[{i+1}/{len(paths)}] Exporting: {path}")
    
    # Run FModel export command
    subprocess.run([FMODEL_PATH, "-export", path], 
                   capture_output=True, 
                   text=True)

print("Export complete!")