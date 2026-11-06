import os
import re

def validate_shared_param_file(filepath):
    if not os.path.exists(filepath):
        print(f"Error: File {filepath} does not exist.")
        return False

    # Revit shared-parameter files in Taiwanese projects are commonly saved as
    # either UTF-8 or Windows Traditional Chinese (CP950). Accept both without
    # rewriting the source file, because Revit itself may depend on its encoding.
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            lines = f.readlines()
    except UnicodeDecodeError:
        with open(filepath, 'r', encoding='cp950') as f:
            lines = f.readlines()

    groups = {}
    params = []
    guids = set()

    for idx, line in enumerate(lines, 1):
        line = line.strip('\r\n')
        if not line or line.startswith('#') or line.startswith('*'):
            continue
        
        tokens = line.split('\t')
        record_type = tokens[0]

        if record_type == 'GROUP':
            if len(tokens) >= 3:
                group_id, group_name = tokens[1], tokens[2]
                groups[group_id] = group_name
        elif record_type == 'PARAM':
            if len(tokens) >= 10:
                guid, name, datatype, datacat, group_id, visible, desc, user_mod, hide_no_val = tokens[1:10]
                if guid in guids:
                    print(f"Syntax Error line {idx}: Duplicate GUID {guid}")
                    return False
                guids.add(guid)
                params.append({
                    "line": idx,
                    "guid": guid,
                    "name": name,
                    "datatype": datatype,
                    "group_id": group_id,
                    "group_name": groups.get(group_id, "Unknown"),
                    "description": desc
                })
            else:
                print(f"Syntax Error line {idx}: Insufficient tokens in PARAM line")
                return False

    print("==========================================================")
    print(f" VALIDATION SUCCESS: {len(params)} Shared Parameters Verified")
    print(f" Groups count: {len(groups)}")
    print("==========================================================")
    for p in params:
        print(f"[{p['group_name']}] {p['name']} ({p['datatype']}) -> {p['description']}")
    return True

if __name__ == '__main__':
    param_path = os.path.join(os.path.dirname(__file__), 'GreenMaterial_SharedParams.txt')
    validate_shared_param_file(param_path)
