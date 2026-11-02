import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../AuthContext'
import { api, type HealthCheck, type HealthReport } from '../api'
import './AdminHealth.css'

const KIND_LABELS: Record<string, string> = {
  site: 'Site',
  search: 'Search',
  episode: 'Episode URL',
  embed: 'Embed',
  links: 'Saved links',
}

const RESULT_LABELS: Record<string, string> = { ok: 'OK', broken: 'Broken', unknown: 'Unknown' }

function when(iso: string) {
  return new Date(iso).toLocaleString()
}

function since(iso: string | null) {
  if (!iso) return ''
  const hours = Math.round((Date.now() - new Date(iso).getTime()) / 3_600_000)
  if (hours < 1) return 'under an hour'
  if (hours < 48) return `${hours}h`
  return `${Math.round(hours / 24)}d`
}

function CheckRow({ check }: { check: HealthCheck }) {
  return (
    <tr className={`health-check health-${check.result}`}>
      <td>{KIND_LABELS[check.kind] ?? check.kind}</td>
      <td>
        <span className={`health-dot health-${check.result}`} aria-hidden="true" />
        {RESULT_LABELS[check.result] ?? check.result}
        {check.httpStatus ? ` (${check.httpStatus})` : ''}
      </td>
      <td>
        {check.detail}
        {check.result === 'broken' && check.consecutiveFailures > 0 && (
          <div className="muted">
            Failing {check.consecutiveFailures} run{check.consecutiveFailures === 1 ? '' : 's'}
            {check.failingSince ? `, since ${when(check.failingSince)}` : ''}
          </div>
        )}
        {check.hint && <div className="health-hint">Fix: {check.hint}</div>}
      </td>
    </tr>
  )
}

export function AdminHealth() {
  const { user } = useAuth()
  const [report, setReport] = useState<HealthReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [probing, setProbing] = useState<string | null>(null)
  const [preview, setPreview] = useState<Record<string, HealthCheck[]>>({})
  const poll = useRef<number | null>(null)

  const load = () => api.getSourceHealth().then(setReport).catch((e) => setError(e instanceof Error ? e.message : 'Could not load the health report.'))

  useEffect(() => {
    if (user?.isAdmin) load()
  }, [user])

  // The daily run is fire-and-forget server-side; poll while it says it's running.
  useEffect(() => {
    if (!report?.running) {
      if (poll.current) window.clearInterval(poll.current)
      poll.current = null
      return
    }

    poll.current = window.setInterval(load, 2000)
    return () => {
      if (poll.current) window.clearInterval(poll.current)
    }
  }, [report?.running])

  if (!user?.isAdmin) {
    return <p role="alert">Not authorized.</p>
  }

  const checkNow = async () => {
    setError(null)
    try {
      await api.checkSources()
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not start a check.')
    }
  }

  // One source, nothing written: shows what its templates do right now.
  const probeOne = async (sourceId: string) => {
    setError(null)
    setProbing(sourceId)
    try {
      const checks = await api.checkOneSource(sourceId)
      setPreview((prev) => ({ ...prev, [sourceId]: checks }))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not check that source.')
    } finally {
      setProbing(null)
    }
  }

  return (
    <div>
      <h1>Source health</h1>
      <p className="muted">
        Every enabled source is probed once a day, and on demand below. <strong>Broken</strong> hides a source's watch buttons after
        three runs in a row; <strong>Unknown</strong> means the answer wasn't conclusive (a bot check, or a page this check can't
        decide) and never hides anything. Nothing here can confirm that a video actually plays.
      </p>
      {error && <p role="alert">{error}</p>}

      <div className="health-actions">
        <button type="button" className="primary" onClick={checkNow} disabled={report?.running}>
          {report?.running ? 'Checking…' : 'Check all sources now'}
        </button>
        <span className="muted">{report?.lastRunAt ? `Last run ${when(report.lastRunAt)} (${since(report.lastRunAt)} ago)` : 'No run recorded yet'}</span>
      </div>

      {report?.sources.map((source) => (
        <section key={source.id} className="card health-source">
          <div className="health-source-head">
            <h2>
              <Link to="/admin/sources">{source.name}</Link>{' '}
              {source.healthy ? (
                source.enabled ? <span className="badge-ok">enabled</span> : <span className="muted">disabled</span>
              ) : (
                <span className="badge-broken">hidden from users</span>
              )}
            </h2>
            <div className="muted">{source.baseUrl}</div>
            <button type="button" onClick={() => probeOne(source.id)} disabled={probing === source.id}>
              {probing === source.id ? 'Checking…' : 'Check this one'}
            </button>
          </div>

          {source.checks.length === 0 && !preview[source.id] && <p className="muted">Not checked yet.</p>}

          {source.checks.length > 0 && (
            <table className="health-table">
              <tbody>
                {source.checks.map((check) => (
                  <CheckRow key={check.kind} check={check} />
                ))}
              </tbody>
            </table>
          )}

          {preview[source.id] && (
            <>
              <h3>Just now, nothing saved</h3>
              <table className="health-table">
                <tbody>
                  {preview[source.id].map((check) => (
                    <CheckRow key={check.kind} check={check} />
                  ))}
                </tbody>
              </table>
            </>
          )}
        </section>
      ))}

      <h2>User reports</h2>
      {report?.reports.length === 0 && <p className="muted">Nobody has reported a broken player yet.</p>}
      {report?.reports.map((r) => (
        <div key={r.id} className="card health-report-row">
          <div>
            <strong>{r.sourceName}</strong> — {r.animeTitle ?? `anime ${r.anilistId ?? '?'}`}
            {r.episode ? `, episode ${r.episode}` : ''}
          </div>
          {r.url && (
            <a href={r.url} target="_blank" rel="noopener noreferrer">
              the URL that failed ↗
            </a>
          )}
          <div className="muted">{when(r.createdAt)}</div>
        </div>
      ))}
    </div>
  )
}
