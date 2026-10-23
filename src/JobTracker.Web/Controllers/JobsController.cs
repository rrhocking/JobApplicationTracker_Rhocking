using JobTracker.Web.Data;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

public class JobsController(IJobRepository jobs) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(JobStatus? status, string? q, CancellationToken cancellationToken)
    {
        var results = await jobs.GetAllAsync(status, q, cancellationToken);
        return View(new JobListViewModel(results, status, q));
    }

    [HttpGet]
    public IActionResult Create() => View(new JobApplication());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobApplication job, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(job);
        }

        await jobs.AddAsync(job, cancellationToken);
        TempData["Message"] = $"Added {job.Title} at {job.Company}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(id, cancellationToken);
        return job is null ? NotFound() : View(job);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, JobApplication job, CancellationToken cancellationToken)
    {
        // The id comes from the URL, never from the posted form.
        job.Id = id;

        if (!ModelState.IsValid)
        {
            return View(job);
        }

        if (!await jobs.UpdateAsync(job, cancellationToken))
        {
            return NotFound();
        }

        TempData["Message"] = $"Saved {job.Title} at {job.Company}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(id, cancellationToken);
        return job is null ? NotFound() : View(job);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        if (!await jobs.DeleteAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Message"] = "Application deleted.";
        return RedirectToAction(nameof(Index));
    }
}
