// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
export function githubClient(token) {
  if (!token) throw new Error('GitHub authentication is not configured.');
  const request = async (method, path, body) => {
    if (!path.startsWith('/repos/')) throw new Error('Only repository API routes are allowed.');
    const response = await fetch(`https://api.github.com${path}`, {
      method, redirect: 'error', signal: AbortSignal.timeout(30000),
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
    // Never echo response bodies, credentials or exception headers into public output.
    if (!response.ok) throw new Error(`GitHub ${method} request failed (${response.status}).`);
    return response.status === 204 ? null : response.json();
  };
  const list = async (path, property) => {
    const result = [];
    for (let page = 1; page <= 20; page++) {
      const data = await request('GET', `${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
      const entries = property ? data[property] : data;
      if (!Array.isArray(entries)) throw new Error('Unexpected GitHub list response.');
      result.push(...entries);
      if (entries.length < 100) return result;
    }
    throw new Error('GitHub pagination limit reached; refusing a partial decision.');
  };
  return { request, list };
}
