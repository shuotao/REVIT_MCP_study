import re

def extract_pure_conversation(in_path, pr_num, out_f):
    out_f.write(f"\n=========================================\n")
    out_f.write(f"=== PR {pr_num} Conversation ===\n")
    out_f.write(f"=========================================\n")
    
    with open(in_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
        
    # Remove script tags and style tags
    content_clean = re.sub(r'<script.*?>.*?</script>', '', content, flags=re.DOTALL)
    content_clean = re.sub(r'<style.*?>.*?</style>', '', content_clean, flags=re.DOTALL)
    content_clean = re.sub(r'<svg.*?>.*?</svg>', '', content_clean, flags=re.DOTALL)
    content_clean = re.sub(r'<path.*?>.*?</path>', '', content_clean, flags=re.DOTALL)
    
    # Split by lines
    lines = content_clean.split("\n")
    
    in_interesting_section = False
    buffer = []
    
    # We want to print segments of text around matching lines.
    # To do this, let's scan all lines and find matching line indices.
    match_indices = []
    for idx, line in enumerate(lines):
        line_stripped = line.strip()
        text_only = re.sub(r'<[^>]+>', ' ', line_stripped).strip()
        text_only = re.sub(r'\s+', ' ', text_only)
        if not text_only:
            continue
        # Keywords to identify comments/reviews from shuotao or admin or PR status change
        if any(kw in text_only.lower() for kw in ["shuotao", "seven777", "commented", "closed this", "混雜", "拆分", "剖面", "穿梁", "排版"]):
            # exclude some false positives like URLs containing these
            if "githubassets.com" in text_only or "githubusercontent.com" in text_only:
                continue
            match_indices.append(idx)
            
    # Print windows of lines around matches
    printed_indices = set()
    for match_idx in match_indices:
        start = max(0, match_idx - 5)
        end = min(len(lines), match_idx + 15)
        
        if any(i in printed_indices for i in range(start, end)):
            # extend printing if it overlaps
            continue
            
        out_f.write(f"\n--- Match context around Line {match_idx+1} ---\n")
        for i in range(start, end):
            l_stripped = lines[i].strip()
            # Clean HTML tag
            text_only = re.sub(r'<[^>]+>', ' ', l_stripped).strip()
            text_only = re.sub(r'\s+', ' ', text_only)
            if text_only:
                out_f.write(f"L{i+1:4d}: {text_only}\n")
                printed_indices.add(i)
        out_f.write("-" * 40 + "\n")

if __name__ == "__main__":
    out_path = r"c:\Users\sn698\Desktop\REVIT_MCP_study\scripts\extracted_conversations.txt"
    with open(out_path, "w", encoding="utf-8") as out_f:
        extract_pure_conversation(r"C:\Users\sn698\.gemini\antigravity-ide\brain\3a1bc530-a38f-45a4-8de6-9a3eba976b4f\.system_generated\steps\35\content.md", 53, out_f)
        extract_pure_conversation(r"C:\Users\sn698\.gemini\antigravity-ide\brain\3a1bc530-a38f-45a4-8de6-9a3eba976b4f\.system_generated\steps\39\content.md", 54, out_f)
    print("Done! Check scripts/extracted_conversations.txt")
