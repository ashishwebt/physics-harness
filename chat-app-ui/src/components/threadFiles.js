export function getVisibleFiles(files = []) {
  if (!Array.isArray(files)) return [];

  return files.filter((file) => {
    if (!file || typeof file !== 'object') return false;
    const path = typeof file.path === 'string' ? file.path.trim() : '';
    return path.length > 0;
  });
}

export function getSelectedFile(files = [], activePath = null) {
  const visible = getVisibleFiles(files);
  if (visible.length === 0) return null;

  if (activePath) {
    const directMatch = visible.find((file) => file.path === activePath);
    if (directMatch) return directMatch;
  }

  return visible[0];
}
