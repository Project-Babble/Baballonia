import os
import sys
import json
import xml.etree.ElementTree as ET

def convert_resw_to_json(resw_path, json_path):
    """Convert a single .resw file to JSON format."""
    tree = ET.parse(resw_path)
    root = tree.getroot()

    json_data = {}

    for data in root.findall("data"):
        key = data.get("name")
        value = data.find("value").text if data.find("value") is not None else ""
        json_data[key] = value

    # Write to JSON file
    with open(json_path, "w", encoding="utf-8") as json_file:
        json.dump(json_data, json_file, ensure_ascii=False, indent=4)

    print(f"Converted: {resw_path} -> {json_path}")

def process_directory(directory):
    """Recursively find all .resw files in a directory and convert them."""
    for root, _, files in os.walk(directory):
        resw_files = [f for f in files if f.endswith(".resw")]

        if resw_files:
            # Get the parent directory name
            parent_dir_name = os.path.basename(root)
            json_path = os.path.join(root, f"{parent_dir_name}.json")

            merged_data = {}

            for file in resw_files:
                resw_path = os.path.join(root, file)
                tree = ET.parse(resw_path)
                root_element = tree.getroot()

                for data in root_element.findall("data"):
                    key = data.get("name")
                    value = data.find("value").text if data.find("value") is not None else ""
                    merged_data[key] = value

            # Write merged JSON file
            with open(json_path, "w", encoding="utf-8") as json_file:
                json.dump(merged_data, json_file, ensure_ascii=False, indent=4)

            print(f"Created: {json_path}")

if __name__ == "__main__":
    input_directory = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()

    if not os.path.isdir(input_directory):
        print(f"Error: {input_directory} is not a valid directory")
        sys.exit(1)

    process_directory(input_directory)
