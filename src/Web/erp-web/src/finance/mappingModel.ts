import type { components } from '../api/schema';

export type MappingRule = components['schemas']['MappingRuleDto'];

/** Sursa unei reguli, ca text (macheta c1-03): contul, intervalul sau lista de conturi românești. */
export function sourceText(rule: Pick<MappingRule, 'sourceKind' | 'sourceAccountCode' | 'sourceFrom' | 'sourceTo' | 'sourceAccounts'>): string {
  switch (rule.sourceKind) {
    case 1:
      return rule.sourceAccountCode ?? '';
    case 2:
      return `${rule.sourceFrom ?? ''} – ${rule.sourceTo ?? ''}`;
    default:
      return rule.sourceAccounts.map((s) => s.code).join(', ');
  }
}

/** Ținta unei reguli, ca text: contul alternativ sau conturile cu procentele lor. */
export function targetText(rule: Pick<MappingRule, 'targets'>): string {
  return rule.targets.length === 1 ? `${rule.targets[0]?.code ?? ''} ${rule.targets[0]?.name ?? ''}`
    : rule.targets.map((x) => `${x.code} · ${x.percent}%`).join('  ·  ');
}

/** Valorile condițiilor unei reguli, ca listă de fragmente de afișat (fără traducere: cheia și parametrii). */
export function conditionParts(rule: MappingRule): readonly { readonly key: string; readonly value?: string }[] {
  const parts: { key: string; value?: string }[] = [];
  if (rule.balanceSign !== 0) parts.push({ key: `map.cond.balance.${rule.balanceSign}` });
  if (rule.side !== 0) parts.push({ key: `map.cond.side.${rule.side}` });
  if (rule.partnerKind != null) parts.push({ key: `chart.partnerKind.${rule.partnerKind}` });
  if (rule.partnerName != null) parts.push({ key: 'map.cond.partner', value: rule.partnerName });
  if (rule.dimensionCondition === 1) parts.push({ key: `chart.dim.${rule.dimensionType ?? ''}`, value: rule.dimensionValueName ?? rule.dimensionValueCode ?? '' });
  if (rule.dimensionCondition === 2) parts.push({ key: `map.cond.dimAny.${rule.dimensionType ?? ''}` });
  if (rule.dimensionCondition === 3) parts.push({ key: `map.cond.dimNone.${rule.dimensionType ?? ''}` });
  if (rule.currencyCode != null) parts.push({ key: 'map.cond.currency', value: rule.currencyCode });
  return parts;
}

/** Ordinea nouă după mutarea unei reguli cu o poziție (sus = -1, jos = +1): prioritățile rămân cele existente, doar regulile își schimbă locul. */
export function moveRule(rules: readonly MappingRule[], id: number, step: -1 | 1): { id: number; priority: number }[] {
  const ordered = [...rules].sort((a, b) => a.priority - b.priority);
  const priorities = ordered.map((r) => r.priority);
  const index = ordered.findIndex((r) => r.id === id);
  const other = index + step;
  const ids = ordered.map((r) => r.id);
  if (index >= 0 && other >= 0 && other < ordered.length) {
    ids[index] = ordered[other]?.id ?? id;
    ids[other] = id;
  }

  return ids.map((ruleId, i) => ({ id: ruleId, priority: priorities[i] ?? 0 }));
}
