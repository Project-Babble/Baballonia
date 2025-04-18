import os
import sys
import xml.etree.ElementTree as ET

def convert_resw_to_resx(resw_path, resx_path):
    """Convert a single .resw file to .resx format."""
    tree = ET.parse(resw_path)
    root = tree.getroot()

    resx_root = ET.Element("root")

    # Add required .resx headers
    headers = {
        "resmimetype": "text/microsoft-resx",
        "version": "2.0",
        "reader": "System.Resources.ResXResourceReader, System.Windows.Forms",
        "writer": "System.Resources.ResXResourceWriter, System.Windows.Forms"
    }

    for name, value in headers.items():
        resheader = ET.SubElement(resx_root, "resheader", {"name": name})
        ET.SubElement(resheader, "value").text = value

    # Convert data entries
    for data in root.findall("data"):
        resx_data = ET.SubElement(resx_root, "data", {"name": data.get("name")})
        value = data.find("value")
        if value is not None:
            ET.SubElement(resx_data, "value").text = value.text

    # Write to .resx file
    resx_tree = ET.ElementTree(resx_root)
    resx_tree.write(resx_path, encoding="utf-8", xml_declaration=True)
    print(f"Converted: {resw_path} -> {resx_path}")

def process_directory(directory):
    """Recursively find all .resw files in a directory and convert them."""
    for root, _, files in os.walk(directory):
        for file in files:
            if file.endswith(".resw"):
                resw_path = os.path.join(root, file)
                resx_path = os.path.splitext(resw_path)[0] + ".resx"  # Change extension to .resx
                convert_resw_to_resx(resw_path, resx_path)

if __name__ == "__main__":
    input_directory = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()

    if not os.path.isdir(input_directory):
        print(f"Error: {input_directory} is not a valid directory")
        sys.exit(1)

    process_directory(input_directory)