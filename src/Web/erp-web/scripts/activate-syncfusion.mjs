// Activeaza licenta Syncfusion din variabila SYNCFUSION_LICENSE (07 Stack tehnologic, ST4). Cheia nu se scrie in depozit.
// Fara variabila (de exemplu un fork fara secret) instalarea continua; componentele arata mesajul de evaluare.
import { spawnSync } from 'node:child_process';

if (!process.env.SYNCFUSION_LICENSE) {
  console.warn('SYNCFUSION_LICENSE lipsește: componentele Syncfusion vor afișa mesajul de evaluare.');
  process.exit(0);
}

const result = spawnSync('npx', ['--no-install', 'syncfusion-license', 'activate'], { stdio: 'inherit', shell: true });
process.exit(result.status ?? 1);
