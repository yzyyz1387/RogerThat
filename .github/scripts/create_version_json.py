import os
import re
import json
import hashlib
import requests
from datetime import datetime

def verify_gitee_release(gitee_url):
    try:
        response = requests.head(gitee_url, timeout=10)
        return response.status_code == 200
    except:
        return False

def get_release_asset_url(owner, repo, tag, token):
    headers = {
        'Authorization': f'token {token}',
        'Accept': 'application/vnd.github.v3+json'
    }
    
    # 获取 GitHub release 信息
    url = f'https://api.github.com/repos/{owner}/{repo}/releases/tags/{tag}'
    response = requests.get(url, headers=headers)
    release_data = response.json()
    
    # 获取 GitHub 资产的 URL
    zip_name = f'RogerThat-{tag}.zip'
    github_url = None
    for asset in release_data['assets']:
        if asset['name'] == zip_name:
            github_url = asset['browser_download_url']
            break
    
    if not github_url:
        return None

    # 生成对应的 Gitee URL（使用正确的用户名）
    gitee_url = github_url.replace('github.com/yzyyz1387', 'gitee.com/yzyyz1387')
    
    # 验证 Gitee URL 是否可用
    if not verify_gitee_release(gitee_url):
        print(f"Warning: Gitee release asset not available: {gitee_url}")
        return {
            'github': github_url  # 如果 Gitee 不可用，只返回 GitHub URL
        }
    
    return {
        'github': github_url,
        'gitee': gitee_url
    }

def calculate_sha256(url, token):
    headers = {
        'Authorization': f'token {token}',
        'Accept': 'application/octet-stream'
    }
    
    response = requests.get(url, headers=headers, stream=True)
    sha256_hash = hashlib.sha256()
    
    for chunk in response.iter_content(chunk_size=4096):
        sha256_hash.update(chunk)
    
    return sha256_hash.hexdigest()

def parse_version(version_str):
    """解析版本号，返回可比较的元组"""
    # 分割主版本号和预发布标识
    parts = version_str.split('-', 1)
    main_version = parts[0]
    pre_release = parts[1] if len(parts) > 1 else ''
    
    # 解析主版本号
    version_parts = [int(x) for x in main_version.split('.')]
    
    # 解析预发布版本
    pre_release_type = ''
    pre_release_num = 0
    if pre_release:
        match = re.match(r'(alpha|beta|rc)(\d+)', pre_release.lower())
        if match:
            pre_release_type = match.group(1)
            pre_release_num = int(match.group(2))
    
    # 预发布类型的权重
    pre_release_weight = {
        '': 3,  # 正式版本
        'rc': 2,
        'beta': 1,
        'alpha': 0
    }
    
    return (
        version_parts,  # (major, minor, patch)
        pre_release_weight.get(pre_release_type, -1),  # 预发布类型权重
        pre_release_num  # 预发布版本号
    )

def parse_version_md():
    with open('version.md', 'r', encoding='utf-8') as f:
        content = f.read()
    
    # 分割成不同的版本块
    version_blocks = re.split(r'\n##\s+', content)
    
    # 解析所有版本信息
    versions = []
    for block in version_blocks[1:]:  # 跳过标题块
        lines = block.strip().split('\n')
        if not lines:
            continue
            
        # 获取版本号
        version = lines[0].strip()
        
        # 获取更新内容
        changelog = []
        for line in lines:
            if line.strip().startswith('- '):
                changelog.append(line.strip()[2:])
        
        if version and changelog:
            versions.append((version, changelog))
    
    # 按版本号排序
    versions.sort(key=lambda x: parse_version(x[0]), reverse=True)
    
    # 返回最新版本信息
    if versions:
        return versions[0]
    return None, []

def main():
    # 获取环境变量
    token = os.environ['GITHUB_TOKEN']
    owner = os.environ['REPO_OWNER']
    repo = os.environ['REPO_NAME']
    tag = os.environ['RELEASE_TAG']
    
    # 解析版本信息
    version, changelog = parse_version_md()
    if not version:
        raise ValueError("在 version.md 中未找到有效的版本号")
    
    # 验证 tag 名是否与版本号匹配
    expected_tag = f"v{version}"  #  tag要总是以 v开头
    if tag != expected_tag:
        raise ValueError(
            f"Release 标签 '{tag}' 与 version.md 中的版本号 '{version}' 不匹配\n"
            f"正确的标签应该是：{expected_tag}"
        )
    
    # 获取下载URL
    download_urls = get_release_asset_url(owner, repo, tag, token)
    if not download_urls:
        raise ValueError(f"未找到对应的发布文件：RogerThat-{tag}.zip")
    
    # 计算SHA256
    sha256 = calculate_sha256(download_urls['github'], token)
    
    # 创建JSON数据
    version_info = {
        'version': version,
        'releaseDate': datetime.now().strftime('%Y-%m-%d'),
        'downloadUrl': download_urls['github'],  # 保持兼容性
        'downloadUrls': download_urls,  # 新增字段，包含两个下载源
        'changelog': changelog,
        'sha256': sha256
    }
    
    # 创建输出目录
    os.makedirs('dist', exist_ok=True)
    
    # 保存JSON文件
    with open('dist/latest.json', 'w', encoding='utf-8') as f:
        json.dump(version_info, f, ensure_ascii=False, indent=2)
    
    # 创建CNAME文件
    with open('dist/CNAME', 'w') as f:
        f.write('update.yzyyz.top')

if __name__ == '__main__':
    main() 