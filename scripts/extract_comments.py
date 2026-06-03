import re

def clean_and_extract_to_file(in_path, pr_num, out_f):
    out_f.write(f"\n=========================================\n")
    out_f.write(f"=== PR {pr_num} Cleaned Comments ===\n")
    out_f.write(f"=========================================\n")
    
    with open(in_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
    
    paragraphs = content.split("\n\n")
    found = False
    for idx, p in enumerate(paragraphs):
        p_clean = p.strip()
        # Search for lines that contain shuotao, closed this, or review text
        if any(keyword in p_clean.lower() for keyword in ["shuotao", "closed this", "混雜", "拆分", "剖面", "上游"]):
            # Check if it's meta tag or script, we can skip if it looks like HTML metadata or script
            if p_clean.startswith("<meta") or p_clean.startswith("<link") or p_clean.startswith("<script") or p_clean.startswith("<svg"):
                continue
            out_f.write(f"[Paragraph {idx}]\n")
            out_f.write(p_clean + "\n")
            out_f.write("-" * 40 + "\n")
            found = True
            
    if not found:
        out_f.write("No matching paragraphs found containing relevant keywords.\n")

if __name__ == "__main__":
    out_path = r"c:\Users\sn698\Desktop\REVIT_MCP_study\scripts\extracted_comments.txt"
    with open(out_path, "w", encoding="utf-8") as out_f:
        clean_and_extract_to_file(r"C:\Users\sn698\.gemini\antigravity-ide\brain\3a1bc530-a38f-45a4-8de6-9a3eba976b4f\.system_generated\steps\35\content.md", 53, out_f)
        clean_and_extract_to_file(r"C:\Users\sn698\.gemini\antigravity-ide\brain\3a1bc530-a38f-45a4-8de6-9a3eba976b4f\.system_generated\steps\39\content.md", 54, out_f)
    print("Extracted to scripts/extracted_comments.txt successfully!")
