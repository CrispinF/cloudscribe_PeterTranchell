$(document).ready(function () {
  $('#psalterindex').DataTable({
        "lengthMenu": [[25, 50, -1], [25, 50, "All"]],
		// make the Chant number not sortable as it gives misleading sort
		"columnDefs": [
		{ "targets": [1], "searchable": false, "orderable": false, "visible": true }
		]
    });
} );
