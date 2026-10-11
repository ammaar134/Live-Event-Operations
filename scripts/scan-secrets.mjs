import { execFileSync } from 'node:child_process';
const git = (...args) => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const history = process.argv.includes('--history');
const refs = history ? git('rev-list', '--all').trim().split('\n').filter(Boolean) : [':'];
const patterns = [
  /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/,
  /(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,})/,
  /\b(?:sk_live_|sk_test_|AKIA)[A-Za-z0-9]{16,}/,
  /[A-Z0-9._%+-]+@(?:gmail|hotmail|outlook|yahoo)\.com/i,
  /Password=[A-Za-z0-9!@#$%]{16,}(?:;|["'\s])/
];
let count = 0;
for (const ref of refs) {
  const files = (ref === ':' ? git('diff', '--cached', '--name-only', '--diff-filter=ACM') : git('ls-tree', '-r', '--name-only', ref)).trim().split('\n').filter(Boolean);
  for (const file of files) {
    if (/(^|\/)(\.env|\.local|\.tools)(\/|$)/.test(file) || /\.(?:pfx|pem|log)$/.test(file)) throw new Error(`Forbidden publication path: ${file}`);
    if (/\.(?:png|jpg|woff2?)$/.test(file)) continue;
    const text = git('show', ref === ':' ? `:${file}` : `${ref}:${file}`);
    if (patterns.some(p => p.test(text))) throw new Error(`Potential secret/private data in ${file}; review required`);
    count++;
  }
}
console.log(`Publication scan passed: ${count} text file versions; ${history ? 'reachable history' : 'staged diff'}. Images require visual review.`);
